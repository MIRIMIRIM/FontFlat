using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class VvarLayout
{
    internal const uint Tag = 0x56564152;
    internal const int HeaderLength = 24, VariationStoreAt = 4, AdvHeightMapAt = 8,
        TsbMapAt = 12, BsbMapAt = 16, VOrgMapAt = 20;
}

// VVAR 1.0 (the vertical glyph metrics variation face): the HvarSchema five-slot
// shape extended by one mapping slot and four header bytes - a packed u32 version,
// the required ItemVariationStore, then four optional DeltaSetIndexMaps, all
// Offset32 from the VVAR start (spec vvar.md L61-67; briefing fixes 3/4 - the
// briefing's "reserved + valueRecordSize" fields are MVAR's, not VVAR's, and the
// mapping names are advanceHeight/tsb/bsb/vOrg, not advHeight/lsb/rsb; every
// corpus host's first subtable starts exactly at offset 24). The Offset32 domain
// carries no 64K pressure, so the write side keeps one canonical layout and no
// second layout candidate exists (the T5-01 registered difference).
//
// The three-branch NULL semantics are this view's hand-written face (card 3,
// OQ-T5-04-4/5), not declared structure: an absent advance map turns glyph IDs
// into implicit delta-set indices (0, gid) exactly "as in the HVAR table"
// (spec L73); absent tsb/bsb maps mean that metric carries no variation - VVAR
// has no lsb/rsb-style advance fallback text and the HVAR-specific fallback is
// not inherited; an absent vOrg map means the vertical origin carries no
// variation (spec L75/L77). Only DeltaSetIndexMap format 0 is defined for the
// mappings (spec L71) and the semantic partial refuses other formats.
[BinaryTable(VvarLayout.Tag, VvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { 0x00010000 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ItemVariationStoreOffset", WireType.UInt32, VvarLayout.VariationStoreAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("AdvanceHeightMappingOffset", WireType.UInt32, VvarLayout.AdvHeightMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("TsbMappingOffset", WireType.UInt32, VvarLayout.TsbMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("BsbMappingOffset", WireType.UInt32, VvarLayout.BsbMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("VOrgMappingOffset", WireType.UInt32, VvarLayout.VOrgMapAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("HeaderEnd", Constant = VvarLayout.HeaderLength)]
[BinaryReference("ItemVariationStore", "ItemVariationStoreOffset", typeof(ItemVariationStoreView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "HeaderEnd")]
[BinaryReference("AdvanceHeightMapping", "AdvanceHeightMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("TsbMapping", "TsbMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("BsbMapping", "BsbMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("VOrgMapping", "VOrgMappingOffset", typeof(DeltaSetIndexMapView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
public readonly ref partial struct VvarView { }
