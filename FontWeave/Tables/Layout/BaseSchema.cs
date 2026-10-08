using FontWeave.Core;
using FontWeave.Schema;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

internal static class BaseLayout
{
    internal const uint Tag = 0x42415345, Version10 = 0x00010000,
        Version11 = 0x00010001;
    internal const int Header10 = 8, Header11 = 12,
        HorizAxisAt = 4, VertAxisAt = 6, ItemVariationStoreAt = 8;
}

// The BASE root (T4-07 section 2): GdefSchema's declared shape with BASE values - a
// versioned header, two Optional/ZeroAllowed axis slots (the vert NULL is a real
// witness, R4; both NULL is the adjudicated legal empty table, OQ-3) and the v1.1
// itemVariationStoreOffset as a 4-byte conditional record with an Offset32 element.
// The typed store target is the second Layout->Variation cross-directory reference
// (the emitter's global:: qualification already carries GDEF's), declared only under
// 1.1 with the shared HeaderEnd floor. The BaselineTag/Script/Values/MinMax/Coord
// subtrees are separate views; the baseline-selection semantics stay outside this
// declaration (the T0 no-shaping-engine ban).
[BinaryTable(BaseLayout.Tag, BaseLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { BaseLayout.Version10, BaseLayout.Version11 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("HorizAxisOffset", WireType.UInt16, BaseLayout.HorizAxisAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("VertAxisOffset", WireType.UInt16, BaseLayout.VertAxisAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryRecord("VariationStoreHeader", 4, WhenVersions = new uint[] { BaseLayout.Version11 })]
[BinaryElement("VariationStoreHeader", "ItemVariationStoreOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
// 8 / 12 by declared version: the reference storage floor (GdefSchema's HeaderEnd shape).
[BinaryComputed("HeaderEnd", "HeaderEndOf", Inputs = new[] { "Version" }, Maximum = 12ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset,
    DiagnosticSource = "Version")]
[BinaryReference("HorizAxis", "HorizAxisOffset", typeof(BaseAxisView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("VertAxis", "VertAxisOffset", typeof(BaseAxisView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
// The store reference trails the two axis references (OQ-T4-02b-4 order); the declared
// existence domain is the record's own v1.1 slot and NULL is the spec's "may be null".
[BinaryReference("ItemVariationStore", "ItemVariationStoreOffset", typeof(ItemVariationStoreView),
    Array = "VariationStoreHeader", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "HeaderEnd",
    WhenVersions = new uint[] { BaseLayout.Version11 })]
public readonly ref partial struct BaseView
{
    private static ReadStatus HeaderEndOf(uint version, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = version == BaseLayout.Version11 ? (ulong)BaseLayout.Header11 : (ulong)BaseLayout.Header10;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
