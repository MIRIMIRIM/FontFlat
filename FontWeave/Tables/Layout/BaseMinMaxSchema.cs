using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseMinMaxLayout
{
    internal const int HeaderLength = 6, MinCoordOffsetField = 0, MaxCoordOffsetField = 2,
        FeatMinMaxCountAt = 4, RecordStride = 8, RecordMinOffsetField = 4,
        RecordMaxOffsetField = 6;
}

// spec MinMax under the family-prefixed name (OQ-7: BaseMinMaxView keeps the generic
// "MinMaxView" free). Four BaseCoord targets on one MinMax-origin basis: the two
// header slots and the two per-row FeatMinMax offsets are all Optional/ZeroAllowed -
// the spec's three "may be NULL" notes with the Example 4B both-NULL positive as the
// synthetic pin (zero real witnesses, R6). featureTag consistency with GSUB/GPOS
// FeatureLists is a cross-table domain this declaration does not touch (section 10).
[BinaryTable(BaseLayout.Tag, BaseMinMaxLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("MinCoordOffset", WireType.UInt16, BaseMinMaxLayout.MinCoordOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("MaxCoordOffset", WireType.UInt16, BaseMinMaxLayout.MaxCoordOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("BaseFeatMinMaxCount", WireType.UInt16, BaseMinMaxLayout.FeatMinMaxCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("FeatMinMaxRecord", "BaseFeatMinMaxCount", BaseMinMaxLayout.HeaderLength,
    BaseMinMaxLayout.RecordStride)]
[BinaryElement("FeatMinMaxRecord", "FeatureTag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("FeatMinMaxRecord", "MinCoordOffset", WireType.UInt16, BaseMinMaxLayout.RecordMinOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("FeatMinMaxRecord", "MaxCoordOffset", WireType.UInt16, BaseMinMaxLayout.RecordMaxOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "BaseFeatMinMaxCount", Stride = BaseMinMaxLayout.RecordStride)]
[BinaryReference("MinCoord", "MinCoordOffset", typeof(BaseCoordView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("MaxCoord", "MaxCoordOffset", typeof(BaseCoordView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("FeatMinCoord", "MinCoordOffset", typeof(BaseCoordView), Array = "FeatMinMaxRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("FeatMaxCoord", "MaxCoordOffset", typeof(BaseCoordView), Array = "FeatMinMaxRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct BaseMinMaxView { }
