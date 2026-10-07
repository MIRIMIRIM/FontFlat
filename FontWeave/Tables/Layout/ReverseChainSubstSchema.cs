using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ReverseChainSubstLayout
{
    internal const int MinimumHeader = 6;
    internal const int CoverageOffsetField = 2, BacktrackGlyphCountField = 4,
        BacktrackArrayAt = 6, OffsetStride = 2;
}

// GSUB lookup type 8 (ReverseChainSingleSubst format 1, the only format). The
// declared surface is the header (format/coverage/backtrack count) plus the
// backtrack coverage-offset counted array from its constant start 6; the lookahead
// coverage chain, the glyph count and the substitutes sit behind computed starts
// (6+2*backtrack and onward) and stay the semantic partial's hand-walked boundary
// (OQ-T4-04-5's pre-registered V1 computed-start limit). Every coverage reference is
// Required (spec defines no NULL coverage here), and the substitute/coverage
// correspondence is the semantic layer's identity check (SingleSubst fmt2 同构).
[BinaryTable(GsubLayout.Tag, ReverseChainSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("SubstFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, ReverseChainSubstLayout.CoverageOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("BacktrackGlyphCount", WireType.UInt16, ReverseChainSubstLayout.BacktrackGlyphCountField,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BacktrackCoverageOffsetRecord", "BacktrackGlyphCount",
    ReverseChainSubstLayout.BacktrackArrayAt, ReverseChainSubstLayout.OffsetStride)]
[BinaryElement("BacktrackCoverageOffsetRecord", "CoverageOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("BacktrackCoverageEnd", Constant = ReverseChainSubstLayout.BacktrackArrayAt,
    Count = "BacktrackGlyphCount", Stride = ReverseChainSubstLayout.OffsetStride)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BacktrackCoverageEnd")]
[BinaryReference("BacktrackCoverage", "CoverageOffset", typeof(CoverageView),
    Array = "BacktrackCoverageOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "BacktrackCoverageEnd")]
public readonly ref partial struct ReverseChainSubstView { }
