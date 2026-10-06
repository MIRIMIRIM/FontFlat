using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class ItemVariationDataLayout
{
    internal const int HeaderLength = 6, ItemCountAt = 0, WordDeltaCountAt = 2,
        RegionIndexCountAt = 4, RegionIndexesAt = 6, RegionIndexStride = 2;
    // The packed wordDeltaCount masks (spec otvarcommonformats.md).
    internal const ushort LongWordsFlag = 0x8000, WordDeltaCountMask = 0x7FFF;
}

// One ItemVariationData subtable: itemCount delta-set rows over regionIndexCount
// regions. RegionIndexes is the declared counted array (relative to the VarData
// start, card 2). WordDeltaCount is declared at its raw wire value - the LONG_WORDS
// flag (0x8000) and the word/short split of the packed delta rows are the card's
// hand-written boundary: the row width formula ((regionIndexCount + wordDeltaCount)
// shorts, doubled when LONG_WORDS is set), the per-row decode and the byte extent are
// semantic-layer work (card 2/3.1), including the LONG_WORDS structural tolerance
// with typed-read refusal at the HVAR consumer (OQ-T5-01-1).
[BinaryTable(0, ItemVariationDataLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ItemCount", WireType.UInt16, ItemVariationDataLayout.ItemCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("WordDeltaCount", WireType.UInt16, ItemVariationDataLayout.WordDeltaCountAt,
    DataField.Flags)]
[BinaryScalar("RegionIndexCount", WireType.UInt16, ItemVariationDataLayout.RegionIndexCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("RegionIndexes", "RegionIndexCount", ItemVariationDataLayout.RegionIndexesAt,
    ItemVariationDataLayout.RegionIndexStride)]
[BinaryElement("RegionIndexes", "RegionIndex", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct ItemVariationDataView { }
