using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class SingleSubstLayout
{
    internal const uint Tag = 0x47535542;
    internal const int MinimumHeader = 4, CoverageOffsetField = 2, BodyOffset = 4;
    internal const int HeaderLength = 6, BodyRecordStride = 2, SubstituteStride = 2;
    internal const int MaximumFormat2Rows = (ushort.MaxValue - HeaderLength) / SubstituteStride;
    // A two-subtable direct Lookup can have a MarkFilteringSet in its 12-byte header.
    internal const int MaximumFirstSplitLength = ushort.MaxValue - 12;
}

[BinaryTable(SingleSubstLayout.Tag, SingleSubstLayout.MinimumHeader, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, SingleSubstLayout.CoverageOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]
[BinaryRecord("DeltaRecord", SingleSubstLayout.BodyRecordStride)]
[BinaryElement("DeltaRecord", "DeltaGlyphId", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("SubstituteHeader", SingleSubstLayout.BodyRecordStride)]
[BinaryElement("SubstituteHeader", "GlyphCount", WireType.UInt16, 0, DataField.Count)]
[BinaryRecord("SubstituteRecord", SingleSubstLayout.SubstituteStride)]
[BinaryElement("SubstituteRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct SingleSubstView { }
