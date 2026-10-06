using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class AttachListLayout
{
    internal const int HeaderLength = 4, CoverageOffsetAt = 0, GlyphCountAt = 2, PointStride = 2;
}

// AttachList: one Coverage plus one AttachPoint offset per covered glyph, in Coverage
// index order. Offsets are Offset16 from the AttachList start; repeated AttachPoint
// offsets legally share one point list (MarkGlyphSets shared-Coverage precedent).
[BinaryTable(GdefLayout.Tag, AttachListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CoverageOffset", WireType.UInt16, AttachListLayout.CoverageOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("GlyphCount", WireType.UInt16, AttachListLayout.GlyphCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryDerived("PointsEnd", Constant = AttachListLayout.HeaderLength,
    Count = "GlyphCount", Stride = AttachListLayout.PointStride)]
[BinaryArray("AttachPointOffsetRecord", "GlyphCount", AttachListLayout.HeaderLength,
    AttachListLayout.PointStride)]
[BinaryElement("AttachPointOffsetRecord", "AttachPointOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "PointsEnd")]
[BinaryReference("AttachPoint", "AttachPointOffset", typeof(AttachPointView),
    Array = "AttachPointOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "PointsEnd")]
public readonly ref partial struct AttachListView { }
