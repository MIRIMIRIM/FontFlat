using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>Mechanical EBLC wire constants (OpenType 1.9.1, the 45424c43.md
/// snapshot - the EBLC chapter is the definitional home of the whole index
/// layer, T6-05's "E" line refs). Every constant here is independent of
/// <c>CblcLayout</c>: the 2.0 version gate (OQ-T6-06-1/-2: each table's own
/// chapter text rules its support face) and the {1,2,4,8} bitDepth set
/// (OQ-T6-06-3: "bit depth: 1, 2, 4, or 8", E L72/L104-111 - no 32 color
/// depth exists in this chapter) are this table's own faces, never reuses of
/// the CBLC-side collections.</summary>
internal static class EblcLayout
{
    internal const uint Tag = 0x45424C43; // 'EBLC'
    // "The first version of the EBLC table is 2.0" (E L54): the version words
    // are the major/minor u16 pair and 2 is the only defined major (the
    // OQ-T6-06-1 ruling: strict {2}; the corpus's 51/51 hosts are all 2.0, so
    // the gate has zero real refusal witnesses - the mirror of CBLC's four
    // 2.0 hosts, OQ-T6-06-2).
    internal const ushort MajorVersion2 = 2;
    // The header is the fixed 8-byte face: u16 majorVersion + u16 minorVersion
    // ("= 0", E L50 - the CFF2 family hard face) + u32 numSizes (E L51 - the
    // CBLC field's same name and meaning).
    internal const int HeaderLength = 8;
    // One BitmapSize row is the 48-byte fixed composite (E L58-73), the
    // counted array at the fixed start 8 - the NameRecord generated-composite
    // line's third exercise (NameRecord → Cblc → Eblc, card T6-06 §2).
    internal const int BitmapSizeAt = 8, BitmapSizeStride = 48;
    // SbitLineMetrics is the 12×1-byte block (E L83-94); the values travel raw
    // with zero interpretation ("not used directly by the rasterizer", E L96).
    internal const int SbitLineMetricsLength = 12;
    internal const int HoriMetricsAt = 16, VertMetricsAt = 28;
    // One IndexSubtableRecord: u16 firstGlyphIndex + u16 lastGlyphIndex +
    // Offset32 indexSubtableOffset (from the IndexSubtableList start, E
    // L179-181) = the 8-byte stride. The IndexSubHeader is a further 8-byte
    // face: u16 indexFormat + u16 imageFormat + Offset32 imageDataOffset
    // ("Offset to image data in EBDT table", E L193-199).
    internal const int IndexSubtableRecordStride = 8, IndexSubHeaderLength = 8;
    // The per-glyph metrics blocks the EBDT frames carry (T L58-79, the same
    // text as the CBDT chapter's): small = 5 bytes (height/width u8 +
    // bearingX/Y i8 + advance u8), big = 8 bytes (height/width u8 + the six
    // bearing/advance words).
    internal const int SmallGlyphMetricsLength = 5, BigGlyphMetricsLength = 8;
    // The index format closed set (E L197/201): 1 and 3 are the offset-array
    // formats (Offset32/Offset16, one extra sentinel entry), 2 and 5 the
    // constant imageSize formats, 4 the sparse glyphID/offset pair array.
    internal const ushort IndexFormat1 = 1, IndexFormat2 = 2, IndexFormat3 = 3,
        IndexFormat4 = 4, IndexFormat5 = 5;
    // The image format faces for this table (card §3, OQ-T6-06-7's reciprocal
    // vocabulary): {1,2,5,6,7,8,9} are the typed EBDT formats; {3,4} are never
    // supported (EBDT obsolete 3, Apple-only 4 - the T0 slice line);
    // {17,18,19} are CBDT-defined formats the EBDT chapter never defines (the
    // inheritance clause D L56 is one-way) and refuse as the unknown class;
    // everything else is unknown. The diagnostic names never cross with the
    // CBLC side's vocabulary (OQ-T6-06-7's line).
    internal const ushort ImageFormat1 = 1, ImageFormat2 = 2, ImageFormat5 = 5,
        ImageFormat6 = 6, ImageFormat7 = 7, ImageFormat8 = 8, ImageFormat9 = 9;
    internal const ushort ImageFormatNeverMin = 3, ImageFormatNeverMax = 4;
    internal const ushort ImageFormatNotDefinedMin = 17, ImageFormatNotDefinedMax = 19;
    // The Bitmap flags contract (E L115-123): bit0 horizontal metrics, bit1
    // vertical metrics, 0xFC "Reserved; set to 0" (the set-to-0 family). The
    // word is an int8 on the wire, so a negative byte is the reserved-bits
    // form; bit0|bit1 both set is not prohibited by any text and stays an
    // observation.
    internal const int FlagsHorizontal = 0x01, FlagsVertical = 0x02,
        FlagsReservedMask = 0xFC;
    /// <summary>The bitDepth closed set (E L72 "1, 2, 4, or 8" + the support
    /// table E L104-111): the OQ-T6-06-3 lean - an independent collection
    /// (the CBLC color depth 32 has no text here) and outside the set refuses
    /// as InvalidValue. Gray depths {2,4,8} are legal (the corpus is all 1,
    /// synthetic pins them).</summary>
    internal static bool IsBitDepthDefined(uint bitDepth) =>
        bitDepth is 1 or 2 or 4 or 8;
    /// <summary>The monochrome observation key (E L125's "For monochrome
    /// bitmaps they should have the values colorRef=0 and bitDepth=1"): the
    /// should face is an observation, never enforced.</summary>
    internal static bool IsMonochrome(uint bitDepth) => bitDepth == 1;
    /// <summary>The index format closed set (E L197/201): {1,2,3,4,5} and
    /// nothing else.</summary>
    internal static bool IsIndexFormatDefined(ushort indexFormat) =>
        indexFormat is IndexFormat1 or IndexFormat2 or IndexFormat3
            or IndexFormat4 or IndexFormat5;
    /// <summary>The typed image formats {1,2,5,6,7,8,9} (card §3): the only
    /// face this table reads; the never-supported {3,4}, the not-defined-in-
    /// EBDT {17,18,19} and the unknown rest answer false and are distinguished
    /// by the diagnostic callers.</summary>
    internal static bool IsImageFormatTyped(ushort imageFormat) =>
        imageFormat is ImageFormat1 or ImageFormat2 or ImageFormat5
            or ImageFormat6 or ImageFormat7 or ImageFormat8 or ImageFormat9;
    internal static bool IsImageFormatNeverSupported(ushort imageFormat) =>
        imageFormat is ImageFormatNeverMin or ImageFormatNeverMax;
    internal static bool IsImageFormatNotDefinedInEbdt(ushort imageFormat) =>
        imageFormat >= ImageFormatNotDefinedMin && imageFormat <= ImageFormatNotDefinedMax;
    /// <summary>The (indexFormat, imageFormat) pairing matrix (card §3,
    /// OQ-T6-06-4's full 35-cell hard refusal): {1,3,4}×{1,2,6,7,8,9} (the
    /// variable-metrics index formats carry their own per-image metrics head)
    /// ∪ {2,5}×{5} (the constant-imageSize formats pair with the
    /// metrics-in-EBLC data-only form - T L139's "for use with EBLC
    /// indexSubTable format 2 or format 5"). Every other pairing refuses as
    /// InvalidFormat: 5 over a variable format leaves the metrics nowhere to
    /// read ({1,3,4}×{5}); {1,2,6,7,8,9} over a constant format
    /// double-declares them ({2,5}×those - the T6-05 OQ-9 principle's 35-cell
    /// exercise).</summary>
    internal static bool IsPairingDefined(ushort indexFormat, ushort imageFormat) =>
        imageFormat is ImageFormat1 or ImageFormat2 or ImageFormat6
            or ImageFormat7 or ImageFormat8 or ImageFormat9
            ? indexFormat is IndexFormat1 or IndexFormat3 or IndexFormat4
            : imageFormat == ImageFormat5
                ? indexFormat is IndexFormat2 or IndexFormat5
                : false;
    /// <summary>One subtable's declared length in bytes over its glyph-domain
    /// size (the card §3 window formula, pinned from E L205-258): the 8-byte
    /// IndexSubHeader plus the format body. n' = last-first+2 keeps the
    /// sentinel entry for formats 1/3; n is the body numGlyphs for 4/5. F2 and
    /// F5 carry the 8-byte big metrics in the body; padding elements are NOT
    /// part of the assumed window (the corpus writes subtables that end flush
    /// or grow the pad element - E L268's line rides the observation side,
    /// the T6-05 OQ-5 lean's second exercise with a compliant real witness
    /// this time: embedded_bitmaps' odd F3 count grows the pad).</summary>
    internal static ulong IndexSubtableLength(ushort indexFormat, ulong spanCount,
        ulong bodyNumGlyphs) => indexFormat switch
    {
        IndexFormat1 => IndexSubHeaderLength + 4UL * spanCount,
        IndexFormat2 => IndexSubHeaderLength + 4UL + BigGlyphMetricsLength,
        IndexFormat3 => IndexSubHeaderLength + 2UL * spanCount,
        IndexFormat4 => IndexSubHeaderLength + 4UL + 4UL * (bodyNumGlyphs + 1UL),
        IndexFormat5 => IndexSubHeaderLength + 4UL + BigGlyphMetricsLength + 4UL +
            2UL * bodyNumGlyphs,
        _ => 0UL,
    };
}

// EBLC v2.0 (card T6-06 §2, OQ-T6-06-1's strict {2}): the single-View line for
// the 'EBLC' tag. The schema declares the three header scalars plus the one
// fixed-start counted 48-byte composite array (bitmapSizes - the NameRecord
// generated-composite line's third exercise): each row's 22 fields travel as
// BinaryElements, with the two SbitLineMetrics blocks expressed as the 12×1-byte
// element runs (the generator's UInt8/Int8 element face covers the byte blocks,
// so no DSL extension and no partial re-parse of the row). The IndexSubtableList
// layer (records, the five index formats, per-glyph window derivation) is
// runtime offset addressing over variable-length nested bodies - outside the
// declaration surface exactly like the CBLC side - so the partial owns that
// walk and no BinaryReference/GenerateStructureScan face exists. The EBLC
// bytes are never opened as a CblcView (different tag, version gate and
// bitDepth set - the AGENTS one-View line's mirror ban).
[BinaryTable(EblcLayout.Tag, EblcLayout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { EblcLayout.MajorVersion2 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("NumSizes", WireType.UInt32, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BitmapSize", "NumSizes", EblcLayout.BitmapSizeAt, EblcLayout.BitmapSizeStride)]
[BinaryElement("BitmapSize", "IndexSubtableListOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryElement("BitmapSize", "IndexSubtableListSize", WireType.UInt32, 4, DataField.Length,
    Semantic = NumberSemantic.Count)]
[BinaryElement("BitmapSize", "NumIndexSubtables", WireType.UInt32, 8, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryElement("BitmapSize", "ColorRef", WireType.UInt32, 12, DataField.Payload)]
// The hori SbitLineMetrics block: 12 bytes of raw metrics (E L83-94), preserved
// verbatim - the rasterizer never reads them directly (E L96).
[BinaryElement("BitmapSize", "HoriAscender", WireType.Int8, 16, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriDescender", WireType.Int8, 17, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriWidthMax", WireType.UInt8, 18, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriCaretSlopeNumerator", WireType.Int8, 19, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriCaretSlopeDenominator", WireType.Int8, 20, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriCaretOffset", WireType.Int8, 21, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriMinOriginSB", WireType.Int8, 22, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriMinAdvanceSB", WireType.Int8, 23, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriMaxBeforeBL", WireType.Int8, 24, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriMinAfterBL", WireType.Int8, 25, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriPad1", WireType.Int8, 26, DataField.Payload)]
[BinaryElement("BitmapSize", "HoriPad2", WireType.Int8, 27, DataField.Payload)]
// The vert SbitLineMetrics block: the same 12-byte run at 28.
[BinaryElement("BitmapSize", "VertAscender", WireType.Int8, 28, DataField.Payload)]
[BinaryElement("BitmapSize", "VertDescender", WireType.Int8, 29, DataField.Payload)]
[BinaryElement("BitmapSize", "VertWidthMax", WireType.UInt8, 30, DataField.Payload)]
[BinaryElement("BitmapSize", "VertCaretSlopeNumerator", WireType.Int8, 31, DataField.Payload)]
[BinaryElement("BitmapSize", "VertCaretSlopeDenominator", WireType.Int8, 32, DataField.Payload)]
[BinaryElement("BitmapSize", "VertCaretOffset", WireType.Int8, 33, DataField.Payload)]
[BinaryElement("BitmapSize", "VertMinOriginSB", WireType.Int8, 34, DataField.Payload)]
[BinaryElement("BitmapSize", "VertMinAdvanceSB", WireType.Int8, 35, DataField.Payload)]
[BinaryElement("BitmapSize", "VertMaxBeforeBL", WireType.Int8, 36, DataField.Payload)]
[BinaryElement("BitmapSize", "VertMinAfterBL", WireType.Int8, 37, DataField.Payload)]
[BinaryElement("BitmapSize", "VertPad1", WireType.Int8, 38, DataField.Payload)]
[BinaryElement("BitmapSize", "VertPad2", WireType.Int8, 39, DataField.Payload)]
[BinaryElement("BitmapSize", "StartGlyphIndex", WireType.UInt16, 40, DataField.GlyphId)]
[BinaryElement("BitmapSize", "EndGlyphIndex", WireType.UInt16, 42, DataField.GlyphId)]
[BinaryElement("BitmapSize", "PpemX", WireType.UInt8, 44, DataField.Payload)]
[BinaryElement("BitmapSize", "PpemY", WireType.UInt8, 45, DataField.Payload)]
[BinaryElement("BitmapSize", "BitDepth", WireType.UInt8, 46, DataField.Payload)]
[BinaryElement("BitmapSize", "Flags", WireType.Int8, 47, DataField.Flags)]
public readonly ref partial struct EblcView { }
