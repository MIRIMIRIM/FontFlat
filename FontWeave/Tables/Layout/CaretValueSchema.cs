using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CaretValueLayout
{
    internal const int PrefixLength = 2, BodyAt = 2, Format3Length = 6, DeviceOffsetAt = 4;
}

// Format 3 is the only format with an owned model (the required static Device keeps its
// M1c reference). Formats 1/2 declare their body records so the LigGlyph chain can read
// them typed (T4-02 format activation): a format-1 coordinate and a format-2 contour
// point index. The point index is opaque UInt16 - its existence domain is the owning
// glyph's outline, a cross-table domain GDEF does not check (OQ-T4-02-2). The format-3
// Device reference (M1c) remains the production extern-At sample: the record is not the
// view (rowPosition=2 only addresses the field), and its zero DeviceOffset keeps the
// semantic-layer refusal below (two-layer coexistence, OQ-M1c-3 - the generated
// InvalidValue row is opener-level only).
[BinaryTable(GdefLayout.Tag, CaretValueLayout.PrefixLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryRecord("Format1Header", 2, WhenVersions = new uint[] { 1 })]
[BinaryElement("Format1Header", "Coordinate", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("Format2Header", 2, WhenVersions = new uint[] { 2 })]
[BinaryElement("Format2Header", "CaretValuePointIndex", WireType.UInt16, 0, DataField.Payload)]
[BinaryRecord("Format3Header", 4, WhenVersions = new uint[] { 3 })]
[BinaryElement("Format3Header", "Coordinate", WireType.Int16, 0, DataField.Payload)]
[BinaryElement("Format3Header", "DeviceOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = CaretValueLayout.Format3Length)]
[BinaryReference("Device", "DeviceOffset", typeof(StaticDeviceView), Array = "Format3Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    ZeroFailure = ReferenceZeroFailure.InvalidValue, Minimum = "BodyEnd",
    WhenVersions = new uint[] { 3 })]
public readonly ref partial struct CaretValueView { }
