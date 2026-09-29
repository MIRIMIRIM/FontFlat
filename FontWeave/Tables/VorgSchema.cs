using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class VorgLayout
{
    internal const uint Tag = 0x564F5247;
    internal const int HeaderLength = 8, RecordStride = 4;
}

[BinaryTable(VorgLayout.Tag, VorgLayout.HeaderLength, VersionField = "MajorVersion", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Version)]
[BinaryScalar("DefaultVertOriginY", WireType.Int16, 4, DataField.Payload)]
[BinaryScalar("NumVertOriginYMetrics", WireType.UInt16, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("OriginRecord", VorgLayout.RecordStride)]
[BinaryElement("OriginRecord", "GlyphIndex", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("OriginRecord", "VertOriginY", WireType.Int16, 2, DataField.Payload)]
public readonly ref partial struct VorgView { }
