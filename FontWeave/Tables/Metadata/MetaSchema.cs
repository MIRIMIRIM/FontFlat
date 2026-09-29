using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class MetaLayout
{
    internal const uint Tag = 0x6D657461;
    internal const int HeaderLength = 16, RecordStride = 12;
}

[BinaryTable(MetaLayout.Tag, MetaLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("Flags", WireType.UInt32, 4, DataField.Flags)]
[BinaryScalar("Reserved", WireType.UInt32, 8, DataField.None)]
[BinaryScalar("DataMapsCount", WireType.UInt32, 12, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryDerived("DirectoryEnd", Constant = MetaLayout.HeaderLength, Count = "DataMapsCount", Stride = MetaLayout.RecordStride)]
[BinaryArray("DataMap", "DataMapsCount", MetaLayout.HeaderLength, MetaLayout.RecordStride)]
[BinaryElement("DataMap", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("DataMap", "DataOffset", WireType.UInt32, 4, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryElement("DataMap", "DataLength", WireType.UInt32, 8, DataField.Length, Semantic = NumberSemantic.Count)]
[BinaryPayload("DataMap", "DataOffset", "DataLength", "DirectoryEnd")]
public readonly ref partial struct MetaView { }
