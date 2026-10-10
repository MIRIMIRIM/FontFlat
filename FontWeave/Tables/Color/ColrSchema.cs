using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class ColrLayout
{
    internal const uint Tag = 0x434F4C52;
    internal const ushort Version0 = 0;
    internal const int HeaderLength = 14;
    internal const int BaseGlyphRecordSize = 6, LayerRecordSize = 4;
    // The layer paletteIndex sentinel: 0xFFFF paints with the text foreground
    // color (spec L688) and is exempt from the cross-table palette-entry domain
    // (OQ-T6-01-4/OQ-T6-01-7; the CPAL labels' 0xFFFF "no string" is a separate
    // contract that happens to share the value).
    internal const ushort ForegroundPaletteIndex = 0xFFFF;
}

// COLR v0 (card T6-01 §2, OQ-T6-01-10): the 14-byte header and the two record
// arrays, both addressed by their explicit Offset32 fields rather than a fixed
// start - the corpus carries both placements (layer-records-first 41/52 and
// base-records-first 11/52), so the declaration walks the offsets and assumes
// no order. Version 1 is the same tag's extension and stays outside the
// supported domain here (UnsupportedVersion at the version field; T6-02 declares
// the v1 face on this one View via WhenVersions, the GDEF single-View line) -
// a v1 wire is refused before any v0 record byte is read, which is exactly the
// fixture discipline for the 175 v1 hosts carrying v0 compatibility records.
[BinaryTable(ColrLayout.Tag, ColrLayout.HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { ColrLayout.Version0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumBaseGlyphRecords", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("BaseGlyphRecordsOffset", WireType.UInt32, 4, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LayerRecordsOffset", WireType.UInt32, 8, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("NumLayerRecords", WireType.UInt16, 12, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaseGlyphRecord", "NumBaseGlyphRecords", 0, ColrLayout.BaseGlyphRecordSize,
    Start = "BaseGlyphRecordsOffset")]
[BinaryElement("BaseGlyphRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("BaseGlyphRecord", "FirstLayerIndex", WireType.UInt16, 2, DataField.Payload)]
[BinaryElement("BaseGlyphRecord", "NumLayers", WireType.UInt16, 4, DataField.Count)]
[BinaryArray("LayerRecord", "NumLayerRecords", 0, ColrLayout.LayerRecordSize,
    Start = "LayerRecordsOffset")]
[BinaryElement("LayerRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("LayerRecord", "PaletteIndex", WireType.UInt16, 2, DataField.Payload)]
public readonly ref partial struct ColrView { }
