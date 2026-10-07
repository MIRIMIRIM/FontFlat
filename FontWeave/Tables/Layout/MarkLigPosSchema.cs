using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MarkLigPosLayout
{
    internal const uint Tag = GposLayout.Tag;
    // The fixed 12-byte header: format, two coverages, classCount, two array offsets.
    internal const int MinimumHeader = 12, MarkCoverageField = 2, LigatureCoverageField = 4,
        ClassCountField = 6, MarkArrayField = 8, LigatureArrayField = 10;
}

// GPOS lookup type 5 (Mark-to-Ligature Attachment, format 1 the only format): the
// fixed 12-byte header declares all six words. The two coverages, the MarkArray and
// the LigatureArray open as Required references over the header-end floor; the
// LigatureAttach bodies behind the array keep their ComponentRecord row walk in the
// semantic partial (OQ-T4-06-1, the shared dynamic-row helper). The only real
// witness is Open Sans VF (facts R2: three subtables, component counts 2..3,
// 36 present-null Anchor fmt3 Devices).
[BinaryTable(MarkLigPosLayout.Tag, MarkLigPosLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "PosFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("PosFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MarkCoverageOffset", WireType.UInt16, MarkLigPosLayout.MarkCoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("LigatureCoverageOffset", WireType.UInt16, MarkLigPosLayout.LigatureCoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("ClassCount", WireType.UInt16, MarkLigPosLayout.ClassCountField,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("MarkArrayOffset", WireType.UInt16, MarkLigPosLayout.MarkArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("LigatureArrayOffset", WireType.UInt16, MarkLigPosLayout.LigatureArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = MarkLigPosLayout.MinimumHeader)]
[BinaryReference("MarkCoverage", "MarkCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("LigatureCoverage", "LigatureCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("MarkArray", "MarkArrayOffset", typeof(MarkArrayView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("LigatureArray", "LigatureArrayOffset", typeof(LigatureArrayView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct MarkLigPosView { }
