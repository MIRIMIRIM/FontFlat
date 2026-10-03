using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class SinglePosLayout
{
    internal const uint Tag = 0x47504F53;
    internal const int MinimumHeader = 6, CoverageField = 2, MaskField = 4;
    internal const int CountField = 6, Format1Rows = 6, Format2Rows = 8, WordStride = 2;
    internal const ushort SupportedMask = 0x00FF;
    internal const int MaximumShortPart = ushort.MaxValue - 12;
}

[BinaryTable(SinglePosLayout.Tag, SinglePosLayout.MinimumHeader, VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, SinglePosLayout.CoverageField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("ValueFormat", WireType.UInt16, SinglePosLayout.MaskField, DataField.Flags)]
[BinaryRecord("ValueCountRecord", SinglePosLayout.WordStride)]
[BinaryElement("ValueCountRecord", "Count", WireType.UInt16, 0, DataField.Count)]
[BinaryRecord("SignedValue", SinglePosLayout.WordStride)]
[BinaryElement("SignedValue", "Value", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("DeviceOffset", SinglePosLayout.WordStride)]
[BinaryElement("DeviceOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct SinglePosView { }
