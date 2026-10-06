using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class ItemVariationStoreLayout
{
    // The dataOffsets array begins directly after the UInt16 count: the bound header is
    // 8 bytes and the declared subtable floor is 8 + 4*count (the three primary
    // witnesses pin regionListOffset == 8 + 4*count; the card's drafted "10+4n" constant
    // was a drafting slip corrected against the frozen wire facts, card 2).
    internal const int HeaderLength = 8, RegionListAt = 2, DataCountAt = 6,
        DataOffsetsAt = 8, DataOffsetStride = 4;
}

// ItemVariationStore format 1 (OT 1.9.1 otvarcommonformats): one VariationRegionList
// plus a counted Offset32 array of ItemVariationData subtables. All offsets are from
// the ItemVariationStore start; every subtable floor is the declared offsets-array end
// (the T4-02 AttachList minimum discipline), so a backward or in-header target is the
// native InvalidOffset face. A NULL dataOffsets entry is spec-legal ("no variation
// data for the outer index") and stays present=false at the reference layer - the
// dereference layer turns it into the no-variation result, not an error (card 3.2).
[BinaryTable(0, ItemVariationStoreLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("VariationRegionListOffset", WireType.UInt32, ItemVariationStoreLayout.RegionListAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("ItemVariationDataCount", WireType.UInt16, ItemVariationStoreLayout.DataCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryDerived("DataOffsetsEnd", Constant = ItemVariationStoreLayout.DataOffsetsAt,
    Count = "ItemVariationDataCount", Stride = ItemVariationStoreLayout.DataOffsetStride)]
[BinaryArray("DataOffsets", "ItemVariationDataCount", ItemVariationStoreLayout.DataOffsetsAt,
    ItemVariationStoreLayout.DataOffsetStride)]
[BinaryElement("DataOffsets", "ItemVariationDataOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryReference("VariationRegionList", "VariationRegionListOffset", typeof(VariationRegionListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "DataOffsetsEnd")]
[BinaryReference("ItemVariationData", "ItemVariationDataOffset", typeof(ItemVariationDataView),
    Array = "DataOffsets", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "DataOffsetsEnd")]
public readonly ref partial struct ItemVariationStoreView { }
