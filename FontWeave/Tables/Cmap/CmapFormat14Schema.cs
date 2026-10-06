using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat14Layout
{
    internal const int HeaderLength = 10, RecordStride = 11,
        DefaultRangeStride = 4, UvsMappingStride = 5;
}

// The selector record count lives on the wire, so the selector rows are declared here
// (contract version 1: the generated accessor owns the ordinal gate and row addressing).
// The two UVS subtable row families stay externally-positioned records: their offsets are
// per-record values into self-relative variable-length tables whose row counts live in the
// subtable headers, so both levels remain owned by the partial (first mixed-form sample).
[BinaryTable(CmapLayout.Tag, CmapFormat14Layout.HeaderLength, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 14 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt32, 2, DataField.Length)]
[BinaryScalar("NumVarSelectorRecords", WireType.UInt32, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("VarSelector", "NumVarSelectorRecords", CmapFormat14Layout.HeaderLength, CmapFormat14Layout.RecordStride)]
[BinaryElement("VarSelector", "VarSelector", WireType.UInt24, 0, DataField.CharCode)]
[BinaryElement("VarSelector", "DefaultUVSOffset", WireType.UInt32, 3, DataField.Offset)]
[BinaryElement("VarSelector", "NonDefaultUVSOffset", WireType.UInt32, 7, DataField.Offset)]
[BinaryRecord("DefaultRange", CmapFormat14Layout.DefaultRangeStride)]
[BinaryElement("DefaultRange", "StartUnicodeValue", WireType.UInt24, 0, DataField.CharCode)]
[BinaryElement("DefaultRange", "AdditionalCount", WireType.UInt8, 3, DataField.Count)]
[BinaryRecord("UvsMapping", CmapFormat14Layout.UvsMappingStride)]
[BinaryElement("UvsMapping", "UnicodeValue", WireType.UInt24, 0, DataField.CharCode)]
[BinaryElement("UvsMapping", "GlyphId", WireType.UInt16, 3, DataField.GlyphId)]
public readonly ref partial struct CmapFormat14View { }
