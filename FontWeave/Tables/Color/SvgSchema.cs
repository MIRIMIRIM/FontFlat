using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class SvgLayout
{
    internal const uint Tag = 0x53564720; // 'SVG ' - the trailing space is data.
    internal const ushort Version0 = 0;
    // The header is the fixed 10-byte face: u16 version ("starting at 0. Set to
    // 0") + Offset32 svgDocumentListOffset ("Must be non-zero", from the table
    // start) + u32 reserved ("Set to 0"), spec V L59-62 - the words pack at
    // 0/2/6 (u16 + Offset32 + u32 = 10B; the frozen census scanner reads the
    // same packing). There is no second defined version (OQ-T6-04-1): the ISO
    // 14496-22 major/minor pair-of-u16 shape is a known external divergence the
    // product does not model (the T6-03 fmt13 line), and reserved is
    // unconditionally present.
    internal const int HeaderLength = 10;
    // The packed header word positions (0/2/6), named for the partial's
    // diagnostic locations.
    internal const int HeaderListOffsetAt = 2, HeaderReservedAt = 6;
    // One SVGDocumentRecord: u16 startGlyphID + u16 endGlyphID +
    // Offset32 svgDocOffset + u32 svgDocLength (spec V L79-82). svgDocOffset is
    // relative to the SVGDocumentList start, NOT the table start (OQ-T6-04-5's
    // pinned read face), and both it and svgDocLength are "Must be non-zero".
    internal const int RecordStride = 12;
    // The numEntries/records/documents region sits behind the header offset at
    // runtime: variable-length nesting over an opaque payload domain is outside
    // the declaration surface (the M2 dynamic-row line), so the partial owns the
    // walk and no BinaryReference/GenerateStructureScan face exists.
}

// SVG v0 (card T6-04 §2, OQ-T6-04-1): the single-View line for the 'SVG ' tag -
// the schema declares the three header scalars and the generator carries the
// storage binding, the mechanical window checks and the ordinal gate; the
// semantic partial (SvgView) reads the runtime-positioned document list. The
// header words pack at 0/2/6: u16 + Offset32 + u32 is the 10-byte face the
// card's section 0 table pins ("= 10B", V L59-62) and the frozen census
// scanner reads - the section 2 prose's "@4/@8" positions are a card slip that
// would make a 12-byte header (the T6-03 fmt13 line: product follows the spec
// snapshot and the frozen oracle; activation-reconciliation note). The
// reserved word is declared as a plain payload scalar: its "Set to 0" contract
// is the OQ-T6-04-2 hard face the read side enforces with the MVAR/MERG
// reserved-word shape. The documentListOffset field is a table-origin Offset32
// per V L61; zero is a legal wire value at the declaration level (the read side
// refuses it as the must-non-zero face) because TryOpen must bind before the
// gate runs.
[BinaryTable(SvgLayout.Tag, SvgLayout.HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { SvgLayout.Version0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("DocumentListOffset", WireType.UInt32, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("Reserved", WireType.UInt32, 6, DataField.Payload)]
public readonly ref partial struct SvgView { }
