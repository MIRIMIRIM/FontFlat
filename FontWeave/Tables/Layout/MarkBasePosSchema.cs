using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MarkBasePosLayout
{
    internal const uint Tag = GposLayout.Tag;
    // The fixed 12-byte header: format, two coverages, classCount, two array offsets.
    internal const int MinimumHeader = 12, MarkCoverageField = 2, BaseCoverageField = 4,
        ClassCountField = 6, MarkArrayField = 8, BaseArrayField = 10;
}

// GPOS lookup type 4 (Mark-to-Base Attachment, format 1 the only format): the fixed
// 12-byte header declares all six words; format 1 is the version (posFormat). The two
// coverages and the MarkArray open as Required references over the header-end floor;
// the BaseArray stays deliberately undeclared - its BaseRecord rows carry the
// classCount-driven dynamic stride (OQ-T4-06-1's generator gap), so the base count
// scalar and the row walk are the semantic partial's hand-written boundary with a
// shared row helper (OQ-T4-06-1's lean resolution: no separate BaseArray view).
[BinaryTable(MarkBasePosLayout.Tag, MarkBasePosLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "PosFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("PosFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MarkCoverageOffset", WireType.UInt16, MarkBasePosLayout.MarkCoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("BaseCoverageOffset", WireType.UInt16, MarkBasePosLayout.BaseCoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("ClassCount", WireType.UInt16, MarkBasePosLayout.ClassCountField,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("MarkArrayOffset", WireType.UInt16, MarkBasePosLayout.MarkArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("BaseArrayOffset", WireType.UInt16, MarkBasePosLayout.BaseArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = MarkBasePosLayout.MinimumHeader)]
[BinaryReference("MarkCoverage", "MarkCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("BaseCoverage", "BaseCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("MarkArray", "MarkArrayOffset", typeof(MarkArrayView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct MarkBasePosView { }
