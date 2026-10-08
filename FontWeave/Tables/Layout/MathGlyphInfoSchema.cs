using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathGlyphInfoLayout
{
    internal const int HeaderLength = 8,
        ItalicsCorrectionInfoOffsetAt = 0, TopAccentAttachmentOffsetAt = 2,
        ExtendedShapeCoverageOffsetAt = 4, KernInfoOffsetAt = 6;
}

// MathGlyphInfo (T4-10 section 2, OQ-1's lean ruling): the four per-glyph family
// pointers are Optional/ZeroAllowed - only extendedShapeCoverage carries the spec's
// "May be NULL" note, but the real wire matrix refuses a Required reading (Partial1's
// all-NULL slots, caudex's three NULLs, DejaVuSans's present-but-all-NULL table), so
// the combination semantics stay the semantic layer's faces. The BodyEnd floor keeps
// all four edges strictly forward of the eight-byte table.
[BinaryTable(MathLayout.Tag, MathGlyphInfoLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ItalicsCorrectionInfoOffset", WireType.UInt16, MathGlyphInfoLayout.ItalicsCorrectionInfoOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("TopAccentAttachmentOffset", WireType.UInt16, MathGlyphInfoLayout.TopAccentAttachmentOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("ExtendedShapeCoverageOffset", WireType.UInt16, MathGlyphInfoLayout.ExtendedShapeCoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("KernInfoOffset", WireType.UInt16, MathGlyphInfoLayout.KernInfoOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("BodyEnd", Constant = MathGlyphInfoLayout.HeaderLength)]
[BinaryReference("ItalicsCorrectionInfo", "ItalicsCorrectionInfoOffset", typeof(MathItalicsCorrectionInfoView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional, Minimum = "BodyEnd")]
[BinaryReference("TopAccentAttachment", "TopAccentAttachmentOffset", typeof(MathTopAccentAttachmentView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional, Minimum = "BodyEnd")]
[BinaryReference("ExtendedShapeCoverage", "ExtendedShapeCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional, Minimum = "BodyEnd")]
[BinaryReference("KernInfo", "KernInfoOffset", typeof(MathKernInfoView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional, Minimum = "BodyEnd")]
public readonly ref partial struct MathGlyphInfoView { }
