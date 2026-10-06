using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class AnchorLayout
{
    internal const uint Tag = 0x47504F53;
    // Formats 1/2 are the 6-byte bound header; format 3 extends it to 10 bytes.
    internal const int MinimumHeader = 6, XCoordinateField = 2, YCoordinateField = 4,
        Format2HeaderField = 6, Format3HeaderField = 6, Format3Length = 10;
}

// Shared anchor leaf (GPOS lookup types 3/4/5/6): this card delivers the structure and
// the typed face reachable through CursivePos; the mark-family consumption is T4-06
// (registered boundary, no mark semantics prebuilt). The format-specific tails are
// per-version records (CaretValue format precedent): format 2's contour point index is
// an opaque UInt16 - its existence domain is the owning glyph's outline, a cross-table
// domain this single-table view does not check (OQ-T4-02-2 precedent, OQ-T4-05-5).
// Format 3's Device offsets are Anchor-relative; either may be NULL while the other
// resolves (spec gpos.md:761, W6's yDevice=0 positive pin), so the target floor is the
// full 10-byte header and offset 0 keeps the present-null semantics of the T4-01
// mask/offset decoupling.
[BinaryTable(AnchorLayout.Tag, AnchorLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "AnchorFormat", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("AnchorFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("XCoordinate", WireType.Int16, AnchorLayout.XCoordinateField, DataField.Payload)]
[BinaryScalar("YCoordinate", WireType.Int16, AnchorLayout.YCoordinateField, DataField.Payload)]
[BinaryRecord("Format2Header", 2, WhenVersions = new uint[] { 2 })]
[BinaryElement("Format2Header", "AnchorPoint", WireType.UInt16, 0, DataField.Payload)]
[BinaryRecord("Format3Header", 4, WhenVersions = new uint[] { 3 })]
[BinaryElement("Format3Header", "XDeviceOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("Format3Header", "YDeviceOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("BodyEnd", Constant = AnchorLayout.Format3Length)]
[BinaryReference("XDevice", "XDeviceOffset", typeof(StaticDeviceView), Array = "Format3Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd", WhenVersions = new uint[] { 3 })]
[BinaryReference("YDevice", "YDeviceOffset", typeof(StaticDeviceView), Array = "Format3Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd", WhenVersions = new uint[] { 3 })]
public readonly ref partial struct AnchorView { }
