using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CaretValueLayout
{
    internal const int PrefixLength = 2, BodyAt = 2, Format3Length = 6, DeviceOffsetAt = 4;
}

// A format-only binding also observes legal 4-byte formats 1/2. Their semantics
// are not implemented; the sole generated body record belongs to typed format 3.
[BinaryTable(GdefLayout.Tag, CaretValueLayout.PrefixLength, VersionField = "Format", SupportedVersions = new uint[] { 3 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryRecord("Format3Header", 4)]
[BinaryElement("Format3Header", "Coordinate", WireType.Int16, 0, DataField.Payload)]
[BinaryElement("Format3Header", "DeviceOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
public readonly ref partial struct CaretValueView { }
