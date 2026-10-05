using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CaretValueLayout
{
    internal const int PrefixLength = 2, BodyAt = 2, Format3Length = 6, DeviceOffsetAt = 4;
}

// Formats 1/2 are known versions with no active record (A2 OQ-1 known-inactive
// precedent): their semantics are not implemented, and the sole generated body record
// belongs to typed format 3. The Device reference (M1c) is the production extern-At
// sample: the record is not the view (rowPosition=2 only addresses the field), and its
// zero DeviceOffset keeps the semantic-layer refusal below (two-layer coexistence,
// OQ-M1c-3 - the generated InvalidValue row is opener-level only).
[BinaryTable(GdefLayout.Tag, CaretValueLayout.PrefixLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
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
