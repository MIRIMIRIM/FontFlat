using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class HvarLayout
{
    internal const uint Tag = 0x48564152;
    internal const int HeaderLength = 20, VariationStoreAt = 4, AdvanceMapAt = 8,
        LsbMapAt = 12, RsbMapAt = 16;
}

// HVAR 1.0 (the variation-line entry table): one required ItemVariationStore plus
// three optional DeltaSetIndexMaps, all Offset32 from the HVAR start. The Offset32
// domain has no 64K pressure, so the write side keeps one canonical layout and no
// second layout candidate exists (the explicit T4-02/03/05 difference, card 7).
// The three maps are optional in both directions: a null offset is the implicit-map
// domain (AdvanceWidthMapping NULL = delta-set index (0, gid), spec hvar.md) and the
// lsb/rsb NULL policy reuses the advance index (OQ-T5-01-2), so zero stays "absent"
// and no reference is Required but possibly-null. Implicit/fallback/sentinel
// dereference semantics are this view's hand-written face (card 3.2), not declared
// structure.
[BinaryTable(HvarLayout.Tag, HvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { 0x00010000 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ItemVariationStoreOffset", WireType.UInt32, HvarLayout.VariationStoreAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("AdvanceWidthMappingOffset", WireType.UInt32, HvarLayout.AdvanceMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LsbMappingOffset", WireType.UInt32, HvarLayout.LsbMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("RsbMappingOffset", WireType.UInt32, HvarLayout.RsbMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("HeaderEnd", Constant = HvarLayout.HeaderLength)]
[BinaryReference("ItemVariationStore", "ItemVariationStoreOffset", typeof(ItemVariationStoreView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "HeaderEnd")]
[BinaryReference("AdvanceWidthMapping", "AdvanceWidthMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("LsbMapping", "LsbMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("RsbMapping", "RsbMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
public readonly ref partial struct HvarView { }
