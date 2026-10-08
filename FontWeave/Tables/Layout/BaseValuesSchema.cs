using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseValuesLayout
{
    internal const int HeaderLength = 4, DefaultBaselineIndexAt = 0,
        BaseCoordCountAt = 2, CoordStride = 2;
}

// One BaseValues: the default baseline index stays a raw payload at the view level -
// its domain (the owning axis's baseTagCount) is the semantic layer's index-domain
// hard check (OQ-2/OQ-4; a NULL tagList on the owning axis narrows the check away).
// The coord-offset array items carry no spec NULL note, so ZeroAllowed=false makes a
// zero entry the native zero-policy refusal (the MinMax slots' explicit contrast);
// the per-item Required reference mirrors the LayoutLookupList LookupOffset shape.
[BinaryTable(BaseLayout.Tag, BaseValuesLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("DefaultBaselineIndex", WireType.UInt16, 0, DataField.Payload)]
[BinaryScalar("BaseCoordCount", WireType.UInt16, BaseValuesLayout.BaseCoordCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaseCoordOffset", "BaseCoordCount", BaseValuesLayout.HeaderLength,
    BaseValuesLayout.CoordStride)]
[BinaryElement("BaseCoordOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("CoordsEnd", Constant = 4, Count = "BaseCoordCount", Stride = BaseValuesLayout.CoordStride)]
[BinaryReference("Coord", "Offset", typeof(BaseCoordView), Array = "BaseCoordOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "CoordsEnd")]
public readonly ref partial struct BaseValuesView { }
