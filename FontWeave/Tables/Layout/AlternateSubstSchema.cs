using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class AlternateSubstLayout
{
    internal const int MinimumHeader = 6, FormatAt = 0, CoverageOffsetField = 2, CountAt = 4,
        OffsetStride = 2;
}

// GSUB lookup type 3 (AlternateSubst format 1, the only format). Same two-reference
// geometry as MultipleSubst: Coverage and AlternateSet offsets share the declared
// RecordsEnd floor (6 + 2*alternateSetCount), and Coverage index i corresponds to
// AlternateSet i (the semantic chain's correspondence check, not a schema relation).
[BinaryTable(GsubLayout.Tag, AlternateSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("SubstFormat", WireType.UInt16, AlternateSubstLayout.FormatAt, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, AlternateSubstLayout.CoverageOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("AlternateSetCount", WireType.UInt16, AlternateSubstLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("AlternateSetOffsetRecord", "AlternateSetCount", AlternateSubstLayout.MinimumHeader,
    AlternateSubstLayout.OffsetStride)]
[BinaryElement("AlternateSetOffsetRecord", "AlternateSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "AlternateSetCount", Stride = AlternateSubstLayout.OffsetStride)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
[BinaryReference("AlternateSet", "AlternateSetOffset", typeof(AlternateSetView), Array = "AlternateSetOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct AlternateSubstView { }
