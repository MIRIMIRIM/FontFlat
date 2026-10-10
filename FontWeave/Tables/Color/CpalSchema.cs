using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CpalLayout
{
    internal const uint Tag = 0x4350414C;
    internal const ushort Version0 = 0, Version1 = 1;
    // The v0 header is the six fixed fields through colorRecordsArrayOffset; the
    // counted colorRecordIndices array follows at a fixed start (12). Version 1
    // appends its three optional Offset32 slots AFTER that array (spec L68-82), so
    // the v1 header region is 24 + 2·numPalettes bytes and the trailing offsets are
    // runtime-positioned, not header constants.
    internal const int V0HeaderLength = 12, IndicesAt = 12, IndexStride = 2,
        V1TailLength = 12;
    // The paletteTypes flag words: two defined bits (usable with a light/dark
    // background, both may be set) and the "Reserved; set to 0" mask 0xFFFC
    // (spec L123-127; the reserved mask is the OQ-T6-01-5 hard face).
    internal const uint PaletteUsableWithLightBackground = 0x0001,
        PaletteUsableWithDarkBackground = 0x0002, PaletteReservedMask = 0xFFFC;
    // The labels arrays' 0xFFFF = "no string" sentinel (spec L135/L143). This is an
    // independent contract from COLR's foreground sentinel (OQ-T6-01-7); the raw
    // nameID is preserved and never dereferenced against the name table here.
    internal const ushort NoStringNameId = 0xFFFF;
}

// CPAL v0/v1 (card T6-01 §2, OQ-T6-01-1): one 12-byte v0 header whose only
// counted array is colorRecordIndices (UInt16 × numPalettes at the fixed start
// 12), plus the BGRA colorRecords block and the three v1 trailing arrays. The
// v0/v1 header split is a tail extension in the GDEF MarkSetsHeader position:
// every v1-only field is declared with WhenVersion={1} so a v0 table refuses
// nothing for physically absent bytes and each generated accessor reads 0
// ("no array") outside its version - the three v1 offsets are "Set to 0 if no
// array is provided" (spec L80-82), so NULL is a legal value on v1 and absence
// on v0, and the zero policy stays the one OQ-T6-01-1 face.
//
// The colorRecords block is deliberately NOT declared as a generated array: its
// offset is a plain u32 with no floor, the L43/L45 minimum-shape and window
// contracts own the addressing, and the rows are byte-faithful BGRA quads read
// in the partial (the M2 dynamic-row boundary). Same for the three v1 trailing
// arrays (u32×palettes / u16×palettes / u16×entries): runtime-addressed by raw
// offset in the partial, no DSL extension for trailing variable arrays.
[BinaryTable(CpalLayout.Tag, CpalLayout.V0HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { CpalLayout.Version0, CpalLayout.Version1 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumPaletteEntries", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("NumPalettes", WireType.UInt16, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("NumColorRecords", WireType.UInt16, 6, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("ColorRecordsArrayOffset", WireType.UInt32, 8, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryArray("ColorRecordIndices", "NumPalettes", CpalLayout.IndicesAt, CpalLayout.IndexStride)]
[BinaryElement("ColorRecordIndices", "Value", WireType.UInt16, 0, DataField.Payload)]
[BinaryDerived("V1OffsetsAt", Constant = CpalLayout.IndicesAt, Count = "NumPalettes",
    Stride = CpalLayout.IndexStride)]
[BinaryScalar("PaletteTypesArrayOffset", WireType.UInt32, 0, DataField.Offset, Position = "V1OffsetsAt",
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = CpalLayout.Version1)]
[BinaryScalar("PaletteLabelsArrayOffset", WireType.UInt32, 4, DataField.Offset, Position = "V1OffsetsAt",
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = CpalLayout.Version1)]
[BinaryScalar("PaletteEntryLabelsArrayOffset", WireType.UInt32, 8, DataField.Offset, Position = "V1OffsetsAt",
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = CpalLayout.Version1)]
public readonly ref partial struct CpalView { }
