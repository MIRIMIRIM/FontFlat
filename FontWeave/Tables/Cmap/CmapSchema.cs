using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapLayout
{
    internal const uint Tag = 0x636D6170;
    internal const int HeaderLength = 4, RecordStride = 8;
}

// Subtable positions/counts beyond the encoding records are bound by the partial:
// record offsets are per-row values, subtable lengths live in each subtable header.
[BinaryTable(CmapLayout.Tag, CmapLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumTables", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("EncodingRecord", "NumTables", CmapLayout.HeaderLength, CmapLayout.RecordStride)]
[BinaryElement("EncodingRecord", "PlatformId", WireType.UInt16, 0, DataField.PlatformId)]
[BinaryElement("EncodingRecord", "EncodingId", WireType.UInt16, 2, DataField.EncodingId)]
[BinaryElement("EncodingRecord", "SubtableOffset", WireType.UInt32, 4, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct CmapView { }
