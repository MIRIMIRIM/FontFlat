using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MarkGlyphSetsLayout
{
    internal const int HeaderLength = 4, CountAt = 2, OffsetStride = 4;
}

[BinaryTable(GdefLayout.Tag, MarkGlyphSetsLayout.HeaderLength, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("SetCount", WireType.UInt16, MarkGlyphSetsLayout.CountAt, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryDerived("SetsEnd", Constant = 4, Count = "SetCount", Stride = 4)]
[BinaryArray("CoverageOffsetRecord", "SetCount", MarkGlyphSetsLayout.HeaderLength, MarkGlyphSetsLayout.OffsetStride)]
[BinaryElement("CoverageOffsetRecord", "CoverageOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("CoverageSet", "CoverageOffset", typeof(CoverageView), Array = "CoverageOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "SetsEnd")]
public readonly ref partial struct MarkGlyphSetsView { }
