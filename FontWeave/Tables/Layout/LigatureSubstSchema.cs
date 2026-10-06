using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigatureSubstLayout
{
    internal const int MinimumHeader = 6, FormatAt = 0, CoverageOffsetField = 2, CountAt = 4,
        OffsetStride = 2;
}

// GSUB lookup type 4 (LigatureSubst format 1, the only format). Same two-reference
// geometry as MultipleSubst/AlternateSubst: Coverage and LigatureSet offsets share the
// declared RecordsEnd floor (6 + 2*ligatureSetCount), and Coverage index i corresponds
// to LigatureSet i (semantic-chain correspondence, W10's negative direction).
[BinaryTable(GsubLayout.Tag, LigatureSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("SubstFormat", WireType.UInt16, LigatureSubstLayout.FormatAt, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, LigatureSubstLayout.CoverageOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("LigatureSetCount", WireType.UInt16, LigatureSubstLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("LigatureSetOffsetRecord", "LigatureSetCount", LigatureSubstLayout.MinimumHeader,
    LigatureSubstLayout.OffsetStride)]
[BinaryElement("LigatureSetOffsetRecord", "LigatureSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "LigatureSetCount", Stride = LigatureSubstLayout.OffsetStride)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
[BinaryReference("LigatureSet", "LigatureSetOffset", typeof(LigatureSetView), Array = "LigatureSetOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct LigatureSubstView { }
