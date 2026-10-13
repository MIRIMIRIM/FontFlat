using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>Mechanical EBDT wire constants (OpenType 1.9.1, the 45424454.md
/// retry snapshot - the "T" line refs).</summary>
internal static class EbdtLayout
{
    internal const uint Tag = 0x45424454; // 'EBDT'
    // "The first version of the EBDT table is version 2.0" (T L52): the same
    // strict major/minor face as the EBLC side (OQ-T6-06-1's ruling rides
    // both gates).
    internal const ushort MajorVersion2 = 2;
    // The header is the fixed 4-byte face: u16 majorVersion + u16 minorVersion
    // ("= 0", T L50). "The rest of the EBDT table is a collection of bitmap
    // data" (T L54) - there is no wrapper: past the header the table is an
    // unstructured pool with zero internal self-addressing, every structure
    // arrives through EBLC indexing, so the pool has no generated face at all
    // (card T6-06 §2; "Long word alignment is not required for these sub
    // tables; byte alignment is sufficient", T L54).
    internal const int HeaderLength = 4;
    // One EbdtComponent (T L83-89): u16 glyphID + i8 xOffset + i8 yOffset =
    // the 4-byte stride. The components' composite resolution ("can be used
    // to look up the location of component glyph data in the EBLC table",
    // T L91) is the application domain - this card reads the array typed and
    // flat, never walks it (OQ-T6-06-6).
    internal const int EbdtComponentStride = 4;
    // Format 8 (T L165-176): smallGlyphMetrics (5B) + one pad byte ("unused,
    // set to 0" on the wire - its value rides the observation side) + u16
    // numComponents + the component array = the 8-byte head before the array.
    internal const int Format8HeadLength = 8;
    // Format 9 (T L178-188): bigGlyphMetrics (8B) + u16 numComponents + the
    // component array = the 10-byte head before the array.
    internal const int Format9HeadLength = 10;
    /// <summary>The EBDT formats this table types (card §3): {1,2,5,6,7,8,9}
    /// - the definition face T6-08 reuses for the CBDT-inherited acceptance
    /// (TABLE-COVERAGE :410). Formats 3 (obsolete, T L123) and 4 (Apple-only
    /// compression, T L127-129) are never supported; 17/18/19 do not exist in
    /// this chapter at all (OQ-T6-06-7's reciprocal vocabulary).</summary>
    internal const ushort ImageFormat1 = 1, ImageFormat2 = 2, ImageFormat5 = 5,
        ImageFormat6 = 6, ImageFormat7 = 7, ImageFormat8 = 8, ImageFormat9 = 9;
}

// EBDT v2.0 (card T6-06 §2): the single-View line for the 'EBDT' tag. The
// schema declares exactly the two version scalars - the 4-byte head is the
// whole generated face, and the unstructured pool behind it has zero declared
// structure (T L54: the formats are indicated by the EBLC index, so pool bytes
// are addressed only through the EBLC-derived windows; declaring any would
// invent addressing the table does not carry). The formats 1/2/5/6/7/8/9
// per-glyph reads are EbdtView partial methods, never a second type (the
// AGENTS one-View-per-table-layout line; the joint glyph-image read holds both
// bounded views explicitly, the T6-05 OQ-12 face's second exercise).
[BinaryTable(EbdtLayout.Tag, EbdtLayout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { EbdtLayout.MajorVersion2 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Payload)]
public readonly ref partial struct EbdtView { }
