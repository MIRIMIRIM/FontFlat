using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MarkMarkPosLayout
{
    internal const uint Tag = GposLayout.Tag;
    // The fixed 12-byte header: format, two coverages, classCount, two array offsets.
    internal const int MinimumHeader = 12, Mark1CoverageField = 2, Mark2CoverageField = 4,
        ClassCountField = 6, Mark1ArrayField = 8, Mark2ArrayField = 10;
}

// GPOS lookup type 6 (Mark-to-Mark Attachment, format 1 the only format): the fixed
// 12-byte header declares all six words. The two coverages and the Mark1 array open
// as Required references; the Mark2Array stays undeclared - its Mark2Record rows
// carry the classCount-driven dynamic stride (OQ-T4-06-1's generator gap), so the
// mark2 count scalar and the row walk are the semantic partial's hand-written
// boundary sharing the type-4 row helper (OQ-T4-06-1's lean resolution: no separate
// Mark2Array view; the two count scalars stay with their host subtable views).
[BinaryTable(MarkMarkPosLayout.Tag, MarkMarkPosLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "PosFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("PosFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Mark1CoverageOffset", WireType.UInt16, MarkMarkPosLayout.Mark1CoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("Mark2CoverageOffset", WireType.UInt16, MarkMarkPosLayout.Mark2CoverageField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("ClassCount", WireType.UInt16, MarkMarkPosLayout.ClassCountField,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("Mark1ArrayOffset", WireType.UInt16, MarkMarkPosLayout.Mark1ArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("Mark2ArrayOffset", WireType.UInt16, MarkMarkPosLayout.Mark2ArrayField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = MarkMarkPosLayout.MinimumHeader)]
[BinaryReference("Mark1Coverage", "Mark1CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("Mark2Coverage", "Mark2CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("Mark1Array", "Mark1ArrayOffset", typeof(MarkArrayView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct MarkMarkPosView { }
