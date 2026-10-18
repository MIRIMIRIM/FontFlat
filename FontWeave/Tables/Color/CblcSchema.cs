using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>Mechanical CBLC wire constants (OpenType 1.9.1, the 43424c43.md
/// snapshot for the head and bitmapSizes; the 45424c43.md EBLC snapshot is the
/// normative home of every index-layer detail - C L71 defers "Remaining details
/// regarding the structure" there).</summary>
internal static class CblcLayout
{
    internal const uint Tag = 0x43424C43; // 'CBLC'
    // The version words are the major/minor u16 pair. 3.0 is the defined head
    // ("The first version of the CBLC table is version 3.0", C L48) and the
    // authority for every structural check; the 2.0 word is accepted alongside
    // it as an alias (2026-10-18 version-gate briefing lean, user-approved:
    // the word is the EBLC generational continuation and no structural
    // difference against 3.0 is known — the former strict-{3} ruling
    // OQ-T6-05-1 and its 2.0 refusal-witness pool are superseded). The write
    // face always emits (3, 0).
    internal const ushort MajorVersion3 = 3, MajorVersion2 = 2;
    // The header is the fixed 8-byte face: u16 majorVersion + u16 minorVersion
    // ("= 0", C L44 - the CFF2 OQ-T5-05-2 family hard face) + u32 numSizes.
    internal const int HeaderLength = 8;
    // One BitmapSize row is the 48-byte fixed composite (C L56-67), the counted
    // array at the fixed start 8 - the NameRecord generated-composite line's
    // second exercise (card T6-05 §2).
    internal const int BitmapSizeAt = 8, BitmapSizeStride = 48;
    // SbitLineMetrics is the 12×1-byte block (E L83-94); the values travel raw
    // with zero interpretation ("not used directly by the rasterizer", E L96).
    internal const int SbitLineMetricsLength = 12;
    internal const int HoriMetricsAt = 16, VertMetricsAt = 28;
    // One IndexSubtableRecord: u16 firstGlyphIndex + u16 lastGlyphIndex +
    // Offset32 indexSubtableOffset (from the IndexSubtableList start, E L179-181)
    // = the 8-byte stride. The IndexSubHeader is a further 8-byte face:
    // u16 indexFormat + u16 imageFormat + Offset32 imageDataOffset ("Offset to
    // image data in EBDT table" - read: the CBDT pool, E L193-199).
    internal const int IndexSubtableRecordStride = 8, IndexSubHeaderLength = 8;
    // The big metrics ride the constant-imageSize subtable bodies (F2/F5,
    // E L214-222/250-258): height/width u8 + the six bearing/advance i8/u8
    // words = the 8-byte block (D L70-77's same face).
    internal const int BigGlyphMetricsLength = 8;
    // The index format closed set (E L197/201): 1 and 3 are the offset-array
    // formats (Offset32/Offset16, one extra sentinel entry), 2 and 5 the
    // constant imageSize formats, 4 the sparse glyphID/offset pair array.
    internal const ushort IndexFormat1 = 1, IndexFormat2 = 2, IndexFormat3 = 3,
        IndexFormat4 = 4, IndexFormat5 = 5;
    // CBDT 3.0 carries PNG 17/18/19 and legacy 1/2/5/6/7/8/9.
    // Obsolete format 3 and Apple-only format 4 remain unsupported.
    internal const ushort ImageFormat17 = 17, ImageFormat18 = 18, ImageFormat19 = 19;
    internal const ushort ImageFormatInheritedMin = 1, ImageFormatInheritedMax = 9;
    internal const ushort ImageFormatNeverMin = 3, ImageFormatNeverMax = 4;
    // The Bitmap flags contract (E L115-123): bit0 horizontal metrics, bit1
    // vertical metrics, 0xFC "Reserved; set to 0" (the set-to-0 family). The
    // word is an int8 on the wire, so a negative byte is the reserved-bits
    // form (equivalent face, OQ-T6-05-7); bit0|bit1 both set is not prohibited
    // by any text and stays an observation.
    internal const int FlagsHorizontal = 0x01, FlagsVertical = 0x02,
        FlagsReservedMask = 0xFC;
    // bitDepth 32 is the BGRA color strike (C L66; D L54's pre-multiplied sRGB
    // note is a consumer-domain annotation); {1,2,4,8} are the gray depths.
    internal const byte BitDepthColor = 32;
    /// <summary>The bitDepth closed set (C L66 "1, 2, 4, or 8" + the color 32).
    /// Outside the set refuses as InvalidValue (OQ-T6-05-7's lean).</summary>
    internal static bool IsBitDepthDefined(uint bitDepth) =>
        bitDepth is 1 or 2 or 4 or 8 or BitDepthColor;
    /// <summary>The color-strike observation key: bitDepth == 32 (the BGRA
    /// pre-multiplied sRGB interpretation is a consumer note, D L54).</summary>
    internal static bool IsColorStrike(uint bitDepth) => bitDepth == BitDepthColor;
    /// <summary>The index format closed set (E L197/201): {1,2,3,4,5} and
    /// nothing else (T6-08 reuses this codec with the same set, card §1).</summary>
    internal static bool IsIndexFormatDefined(ushort indexFormat) =>
        indexFormat is IndexFormat1 or IndexFormat2 or IndexFormat3
            or IndexFormat4 or IndexFormat5;
    /// <summary>CBDT 3.0 supports PNG and the OpenType legacy bitmap formats.
    /// Obsolete format 3, Apple-only format 4 and unknown formats refuse.</summary>
    internal static bool IsImageFormatTyped(ushort imageFormat) =>
        IsImageFormatPng(imageFormat) || IsImageFormatInherited(imageFormat);
    internal static bool IsImageFormatPng(ushort imageFormat) =>
        imageFormat is ImageFormat17 or ImageFormat18 or ImageFormat19;
    internal static bool IsImageFormatInherited(ushort imageFormat) =>
        imageFormat >= ImageFormatInheritedMin && imageFormat <= ImageFormatInheritedMax
            && imageFormat is not (ImageFormatNeverMin or ImageFormatNeverMax);
    internal static bool IsImageFormatNeverSupported(ushort imageFormat) =>
        imageFormat is ImageFormatNeverMin or ImageFormatNeverMax;
    /// <summary>Variable index formats 1/3/4 pair with image 1/2/6/7/8/9/17/18;
    /// constant formats 2/5 pair with image 5/19. The remaining cells refuse.</summary>
    internal static bool IsPairingDefined(ushort indexFormat, ushort imageFormat) =>
        imageFormat is 1 or 2 or 6 or 7 or 8 or 9 or ImageFormat17 or ImageFormat18
            ? indexFormat is IndexFormat1 or IndexFormat3 or IndexFormat4
            : imageFormat is 5 or ImageFormat19
                ? indexFormat is IndexFormat2 or IndexFormat5
                : false;
    /// <summary>One subtable's declared length in bytes over its glyph-domain
    /// size (the card §3 window formula, pinned from E L205-258): the 8-byte
    /// IndexSubHeader plus the format body. n' = last-first+2 keeps the
    /// sentinel entry for formats 1/3; n is the body numGlyphs for 4/5. F2 and
    /// F5 carry the 8-byte big metrics in the body; padding elements are NOT
    /// part of the assumed window (the corpus writes subtables that end flush,
    /// E L268's line rides the observation side, OQ-T6-05-5).</summary>
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

// CBLC v3.0 (card T6-05 §2, OQ-T6-05-1's strict {3}): the single-View line for
// the 'CBLC' tag. The schema declares the three header scalars plus the one
// fixed-start counted 48-byte composite array (bitmapSizes - the NameRecord
// generated-composite line's second exercise): each row's 22 fields travel as
// BinaryElements, with the two SbitLineMetrics blocks expressed as the 12×1-byte
// element runs (the generator's UInt8/Int8 element face covers the byte blocks,
// so no DSL extension and no partial re-parse of the row - the OQ-T6-05-14
// precheck's decided form). The IndexSubtableList layer (records, the five
// index formats, per-glyph window derivation) is runtime offset addressing over
// variable-length nested bodies - outside the declaration surface exactly like
// sbix's strike rows and SVG's document records (the M2 dynamic-row boundary),
// so the partial owns that walk and no BinaryReference/GenerateStructureScan
// face exists (the tables carry no typed subtable DAG, OQ-T6-05-15).
[BinaryTable(CblcLayout.Tag, CblcLayout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { CblcLayout.MajorVersion2, CblcLayout.MajorVersion3 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("NumSizes", WireType.UInt32, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BitmapSize", "NumSizes", CblcLayout.BitmapSizeAt, CblcLayout.BitmapSizeStride)]
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
public readonly ref partial struct CblcView { }
