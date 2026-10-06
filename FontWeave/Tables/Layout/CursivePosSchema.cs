using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CursivePosLayout
{
    internal const uint Tag = 0x47504F53;
    internal const int MinimumHeader = 6, CoverageField = 2, CountField = 4,
        RecordStride = 4, EntryOffsetAt = 0, ExitOffsetAt = 2;
}

// GPOS lookup type 3 (cursive attachment positioning, format 1 - the only format).
// One EntryExit record per covered glyph, in Coverage index order; both anchor slots
// may be NULL, individually or together, meaning no positioning adjustment for that
// side (spec gpos.md:374; the both-NULL record is the adjudicated legal no-op,
// OQ-T4-05-4). The anchor offsets share the declared RecordsEnd storage floor.
[BinaryTable(CursivePosLayout.Tag, CursivePosLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "CursiveFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("CursiveFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, CursivePosLayout.CoverageField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("EntryExitCount", WireType.UInt16, CursivePosLayout.CountField, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryRecord("EntryExitRecord", CursivePosLayout.RecordStride)]
[BinaryElement("EntryExitRecord", "EntryAnchorOffset", WireType.UInt16, CursivePosLayout.EntryOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryElement("EntryExitRecord", "ExitAnchorOffset", WireType.UInt16, CursivePosLayout.ExitOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("RecordsEnd", Constant = CursivePosLayout.MinimumHeader,
    Count = "EntryExitCount", Stride = CursivePosLayout.RecordStride)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
[BinaryReference("EntryAnchor", "EntryAnchorOffset", typeof(AnchorView), Array = "EntryExitRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("ExitAnchor", "ExitAnchorOffset", typeof(AnchorView), Array = "EntryExitRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct CursivePosView { }
