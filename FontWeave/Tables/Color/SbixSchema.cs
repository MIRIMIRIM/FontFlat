using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class SbixLayout
{
    internal const uint Tag = 0x73626978; // 'sbix'
    internal const ushort Version1 = 1;
    // The header is the fixed 8-byte face: u16 version ("set to 1") + u16 flags
    // + u32 numStrikes (spec B L49-54); the counted strikeOffsets array follows
    // at the fixed start 8 (u32 x numStrikes, each offset from the table start -
    // the CPAL colorRecordIndices generated-array line).
    internal const int HeaderLength = 8, StrikeOffsetsAt = 8, StrikeOffsetStride = 4;
    // One strike: u16 ppem + u16 ppi + Offset32 glyphDataOffsets[numGlyphs+1],
    // all offsets from the strike start (spec B L66-72). The array carries one
    // entry per glyph ID plus one end sentinel; numGlyphs is the maxp authority
    // and arrives as the explicit ValidateTable/TryReadTable input (T1-01's
    // MaxpTable.NumGlyphs - T5-04/T5-05's dual-input line, no default value).
    internal const int StrikeHeaderLength = 4, GlyphDataOffsetStride = 4;
    // One GlyphData: i16 originOffsetX + i16 originOffsetY + Tag graphicType +
    // opaque data[] (spec B L86-89) - an 8-byte fixed head over an opaque
    // payload; the payload length is the offsets delta minus 8, and the origin
    // offsets travel as raw placement values (never interpreted, B L91).
    internal const int GlyphDataHeaderLength = 8;
    // The graphicType closed set (spec B L88/L99): 'jpg ', 'png ', 'tiff' and
    // the special 'dupe'. Apple-only 'pdf '/'mask' are outside the OpenType
    // support face ("are not supported in the OpenType specification", B L103)
    // and refuse under the same UnsupportedFormat face (OQ-T6-04-12).
    internal const uint GraphicTypeJpg = 0x6A706720, GraphicTypePng = 0x706E6720,
        GraphicTypeTiff = 0x74696666, GraphicTypeDupe = 0x64757065;
    // The flags contract (spec B L50-52, OQ-T6-04-8): bit0 "For historical
    // reasons ... should always be set to 1" is the read-side hard face (the T0
    // slice check item); bit1 "draw outlines" is an application drawing
    // instruction (B L58/L60, observation only); bits 2-15 are "reserved
    // (set to 0)" - the MVAR/MERG reserved-word refusal family.
    internal const ushort FlagsBit0 = 0x0001, FlagsBit1 = 0x0002,
        FlagsReservedMask = 0xFFFC;

    /// <summary>The graphicType closed set's membership (OQ-T6-04-12): only
    /// 'jpg ', 'png ', 'tiff' and 'dupe' are OpenType-defined; Apple-only
    /// 'pdf '/'mask' and unknown tags refuse as UnsupportedFormat.</summary>
    internal static bool IsGraphicTypeDefined(uint graphicType) =>
        graphicType is GraphicTypeJpg or GraphicTypePng or GraphicTypeTiff
            or GraphicTypeDupe;

    /// <summary>The empty-slot dual form (OQ-T6-04-11): a NULL anchor
    /// (offset 0, Apple's "no data") or a zero span (the OT L72 length
    /// derivation over equal consecutive offsets) - both normalize to empty.</summary>
    internal static bool IsEmptySlot(uint anchor, uint next) =>
        anchor == 0 || next == 0 || anchor == next;
}

// sbix v1 (card T6-04 §2, OQ-T6-04-9): the single-View line for the 'sbix' tag.
// The schema declares the four header scalars plus the one fixed-start counted
// u32 array (strikeOffsets) - the CPAL generated-array face; the strike bodies
// (ppem/ppi head, the numGlyphs+1 glyphDataOffsets array, the GlyphData rows
// over opaque payloads) are runtime-addressed dynamic rows behind each strike
// offset and stay in the partial, exactly like CPAL's colorRecords block. No
// BinaryReference/GenerateStructureScan face exists: there is no typed subtable
// DAG (the strikes are not schemas), and numStrikes==0 stays a legal pure
// header-shell table (the spec has no non-zero clause, OQ-T6-04-9's lean).
[BinaryTable(SbixLayout.Tag, SbixLayout.HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { SbixLayout.Version1 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Flags", WireType.UInt16, 2, DataField.Flags)]
[BinaryScalar("NumStrikes", WireType.UInt32, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("StrikeOffsets", "NumStrikes", SbixLayout.StrikeOffsetsAt, SbixLayout.StrikeOffsetStride)]
[BinaryElement("StrikeOffsets", "Value", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct SbixView { }
