using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>Mechanical CBDT wire constants (OpenType 1.9.1, the 43424454.md
/// snapshot for the head and image formats 17-19).</summary>
internal static class CbdtLayout
{
    internal const uint Tag = 0x43424454; // 'CBDT'
    // "The first version of the CBDT table is version 3.0" (D L48): the same
    // strict major/minor face as CBLC (OQ-T6-05-1's ruling rides both gates).
    internal const ushort MajorVersion3 = 3;
    // The header is the fixed 4-byte face: u16 majorVersion + u16 minorVersion
    // ("= 0", D L46). "The rest of the CBDT table is a collection of bitmap
    // data" (D L50) - there is no bitmapGlyphs wrapper: past the header the
    // table is an unstructured pool with zero internal self-addressing, every
    // structure arrives through CBLC indexing, so the pool has no generated
    // face at all (card T6-05 §2; "byte alignment is sufficient", D L50).
    internal const int HeaderLength = 4;
    // Format 17 (D L93-101): smallGlyphMetrics (5B: height/width u8 +
    // bearingX/Y i8 + advance u8) + u32 dataLen + the raw PNG data.
    internal const int SmallGlyphMetricsLength = 5, Format17HeadLength = 9;
    // Format 18 (D L103-111): bigGlyphMetrics (8B: height/width u8 +
    // horiBearingX/Y i8 + horiAdvance u8 + vertBearingX/Y i8 + vertAdvance u8)
    // + u32 dataLen + the raw PNG data.
    internal const int BigGlyphMetricsLength = 8, Format18HeadLength = 12;
    // Format 19 (D L113-120): u32 dataLen + data ("the metrics are stored in
    // the CBLC table" - the constant-metrics pairing line, OQ-T6-05-9).
    internal const int Format19HeadLength = 4;
    /// <summary>One typed image format's fixed head length before the data
    /// length word's payload (the per-glyph window derivation's anchor).</summary>
    internal static int ImageHeadLength(ushort imageFormat) => imageFormat switch
    {
        CblcLayout.ImageFormat17 => Format17HeadLength,
        CblcLayout.ImageFormat18 => Format18HeadLength,
        _ => Format19HeadLength,
    };
}

// CBDT v3.0 (card T6-05 §2): the single-View line for the 'CBDT' tag. The
// schema declares exactly the two version scalars - the 4-byte head is the
// whole generated face, and the unstructured pool behind it has zero declared
// structure (D L50: the formats are indicated by the CBLC index, so pool bytes
// are addressed only through the CBLC-derived windows; declaring any would
// invent addressing the table does not carry). The formats 17/18/19 per-glyph
// reads are CbdtView partial methods, never a second type (the AGENTS
// one-View-per-table-layout line; the joint glyph-image read holds both
// bounded views explicitly, OQ-T6-05-12).
[BinaryTable(CbdtLayout.Tag, CbdtLayout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { CbdtLayout.MajorVersion3 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Payload)]
public readonly ref partial struct CbdtView { }
