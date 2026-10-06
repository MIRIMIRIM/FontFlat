using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MultipleSubstLayout
{
    internal const int MinimumHeader = 6, FormatAt = 0, CoverageOffsetField = 2, CountAt = 4,
        OffsetStride = 2;
}

// GSUB lookup type 2 (MultipleSubst format 1, the only format). Same two-reference
// geometry as SingleSubst: the Coverage and the per-coverage-index Sequence offsets
// share the declared RecordsEnd storage floor (LayoutTagList derived-count form,
// 6 + 2*sequenceCount), so both offset families refuse storage inside the header in
// one declared place instead of a hand-written pre-check.
[BinaryTable(GsubLayout.Tag, MultipleSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("SubstFormat", WireType.UInt16, MultipleSubstLayout.FormatAt, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, MultipleSubstLayout.CoverageOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("SequenceCount", WireType.UInt16, MultipleSubstLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("SequenceOffsetRecord", "SequenceCount", MultipleSubstLayout.MinimumHeader,
    MultipleSubstLayout.OffsetStride)]
[BinaryElement("SequenceOffsetRecord", "SequenceOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "SequenceCount", Stride = MultipleSubstLayout.OffsetStride)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
[BinaryReference("Sequence", "SequenceOffset", typeof(SequenceView), Array = "SequenceOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct MultipleSubstView { }
