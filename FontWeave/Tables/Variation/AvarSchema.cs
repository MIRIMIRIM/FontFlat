using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class AvarLayout
{
    internal const uint Tag = 0x61766172;
    internal const uint Version10 = 0x00010000;
    internal const int HeaderLength = 8, ReservedAt = 4, AxisCountAt = 6;
    internal const int SegmentMapCountAt = 0, SegmentMapFirstPairAt = 2, SegmentPairStride = 4;
}

// avar 1.0 (the axis normalization modifier, T5-02): an 8-byte header - version
// (packed major.minor, 1.0 only; majorVersion 2 is the varStore-based avar 2.0 and
// refuses at the native UnsupportedVersion gate, with the Amstelvar-avar2.A and
// avar2checker witnesses double-pinning that boundary), the permanently reserved
// zero, and the axisCount that equals the fvar axis count through the explicit
// ValidateTable input contract (the T5-01 IVS shape; card 1.2-III keeps the already
// accepted HVAR/GDEF signatures untouched).
//
// The segment maps are the card's hand-written boundary and are not declared: each
// axis carries a positionMapCount and that many 4-byte F2DOT14 pairs, so no counted
// BinaryArray with a fixed stride can express the walk (card 2). The tolerance
// domain - a map missing the required -1/0/+1 pairs means "no modification to axis
// coordinate values for that axis", and non-increasing fromCoordinate / decreasing
// toCoordinate records "may be ignored" - is spec-defined behavior, not corruption:
// read-side validation stays structural and the tolerance semantics live in the
// observation face and the mapping evaluator (card 1.2-I).
[BinaryTable(AvarLayout.Tag, AvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { AvarLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, AvarLayout.ReservedAt, DataField.Payload)]
[BinaryScalar("AxisCount", WireType.UInt16, AvarLayout.AxisCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
public readonly ref partial struct AvarView { }
