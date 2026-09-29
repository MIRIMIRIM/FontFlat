using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class VdmxLayout
{
    internal const uint Tag = 0x56444D58;
    internal const int HeaderLength = 6, RatioStride = 4, OffsetStride = 2;
    internal const int GroupHeaderLength = 4, EntryStride = 6;
    internal static int DirectoryEnd(int ratios) => HeaderLength + ratios * (RatioStride + OffsetStride);
    internal static int GroupLength(int entries) => GroupHeaderLength + entries * EntryStride;
}

[BinaryTable(VdmxLayout.Tag, VdmxLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 0, 1 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumRecs", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("NumRatios", WireType.UInt16, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("Ratio", "NumRatios", VdmxLayout.HeaderLength, VdmxLayout.RatioStride)]
[BinaryElement("Ratio", "CharSet", WireType.UInt8, 0, DataField.Payload)]
[BinaryElement("Ratio", "XRatio", WireType.UInt8, 1, DataField.Payload)]
[BinaryElement("Ratio", "YStartRatio", WireType.UInt8, 2, DataField.Payload)]
[BinaryElement("Ratio", "YEndRatio", WireType.UInt8, 3, DataField.Payload)]
[BinaryDerived("GroupOffsetsStart", Constant = VdmxLayout.HeaderLength, Count = "NumRatios", Stride = VdmxLayout.RatioStride)]
[BinaryArray("GroupOffset", "NumRatios", 0, VdmxLayout.OffsetStride, Start = "GroupOffsetsStart")]
[BinaryElement("GroupOffset", "Offset", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("DirectoryEnd", Base = "GroupOffsetsStart", Count = "NumRatios", Stride = VdmxLayout.OffsetStride)]
[BinaryRecord("GroupHeader", VdmxLayout.GroupHeaderLength)]
[BinaryElement("GroupHeader", "NumEntries", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryElement("GroupHeader", "StartSize", WireType.UInt8, 2, DataField.Payload)]
[BinaryElement("GroupHeader", "EndSize", WireType.UInt8, 3, DataField.Payload)]
[BinaryRecord("Entry", VdmxLayout.EntryStride)]
[BinaryElement("Entry", "YPelHeight", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("Entry", "YMax", WireType.Int16, 2, DataField.Payload)]
[BinaryElement("Entry", "YMin", WireType.Int16, 4, DataField.Payload)]
public readonly ref partial struct VdmxView { }
