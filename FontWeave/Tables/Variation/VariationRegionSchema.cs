using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class VariationRegionLayout
{
    internal const int HeaderLength = 4, AxisCountAt = 0, RegionCountAt = 2;
}

// VariationRegionList: axisCount plus regionCount, followed by regionCount
// VariationRegion records of axisCount F2Dot14 (start, peak, end) triples each. The
// record stride is axisCount*6 - a dynamic stride the schema language deliberately
// does not declare - so the region walk, the byte extent and the ordered-triple value
// domain (|v| <= 1, start <= peak <= end, the non-opposing-signs rule with the
// peak==0 exception) are this view's hand-written face (card 2/3.1). The two declared
// counts stay typed: regionCount's reserved high bits are checked in the semantic
// layer ("must be cleared", spec otvarcommonformats.md).
[BinaryTable(0, VariationRegionLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("AxisCount", WireType.UInt16, VariationRegionLayout.AxisCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("RegionCount", WireType.UInt16, VariationRegionLayout.RegionCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
public readonly ref partial struct VariationRegionListView { }
