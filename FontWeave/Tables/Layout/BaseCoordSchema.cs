using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseCoordLayout
{
    // Formats 1/2 are the 4-byte bound header; format 3 extends it to 6 bytes.
    internal const int MinimumHeader = 4, CoordinateField = 2,
        Format2HeaderField = 4, Format3HeaderField = 4, Format3Length = 6;
}

// The BaseCoord leaf (AnchorSchema's per-version record shape, T4-07 section 2.1):
// the coordinate is a signed Int16 payload (negative values are real witnesses, R6).
// Format 2's referenceGlyph and baseCoordPoint are payload words at the view level;
// the glyph-id domain of referenceGlyph is the semantic layer's numGlyphs-index hard
// check (OQ-5 - the difference against Anchor fmt2's opaque contour point is
// registered, the point stays opaque). Format 3's single Device slot is
// Optional/ZeroAllowed with the present-null semantics of Anchor fmt3 (W6); its
// 0x8000 VariationIndex target resolves against THIS table's own item variation
// store (OQ-6: no cross-table carrier), a consumption-chain fact the wire-level
// declaration does not encode.
[BinaryTable(BaseLayout.Tag, BaseCoordLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "BaseCoordFormat", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("BaseCoordFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Coordinate", WireType.Int16, BaseCoordLayout.CoordinateField, DataField.Payload)]
[BinaryRecord("Format2Header", 4, WhenVersions = new uint[] { 2 })]
[BinaryElement("Format2Header", "ReferenceGlyph", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("Format2Header", "BaseCoordPoint", WireType.UInt16, 2, DataField.Payload)]
[BinaryRecord("Format3Header", 2, WhenVersions = new uint[] { 3 })]
[BinaryElement("Format3Header", "DeviceOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("BodyEnd", Constant = BaseCoordLayout.Format3Length)]
[BinaryReference("Device", "DeviceOffset", typeof(StaticDeviceView), Array = "Format3Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd", WhenVersions = new uint[] { 3 })]
public readonly ref partial struct BaseCoordView { }
