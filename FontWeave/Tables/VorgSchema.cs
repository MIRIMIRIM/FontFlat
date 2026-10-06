using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class VorgLayout
{
    internal const uint Tag = 0x564F5247;
    internal const int HeaderLength = 8, RecordStride = 4;
}

// VORG is a versioned header followed by a counted array of origin records. Both the
// version and the count live on the wire, so the counted array is declared here
// (contract version 1: the generated accessor owns the ordinal gate and row addressing)
// and the glyph-count and ordering rules are applied by the partial.
[BinaryTable(VorgLayout.Tag, VorgLayout.HeaderLength, ContractVersion = 1, VersionField = "MajorVersion", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Version)]
[BinaryScalar("DefaultVertOriginY", WireType.Int16, 4, DataField.Payload)]
[BinaryScalar("NumVertOriginYMetrics", WireType.UInt16, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("OriginRecord", "NumVertOriginYMetrics", VorgLayout.HeaderLength, VorgLayout.RecordStride)]
[BinaryElement("OriginRecord", "GlyphIndex", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("OriginRecord", "VertOriginY", WireType.Int16, 2, DataField.Payload)]
public readonly ref partial struct VorgView { }
