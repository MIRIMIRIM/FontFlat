using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CoverageLayout
{
    internal const int HeaderLength = 4, CountOffset = 2;
    internal const int GlyphStride = 2, RangeStride = 6;
    internal const int RangeEndOffset = 2, RangeIndexOffset = 4;
}

[BinaryTable(0, CoverageLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Count", WireType.UInt16, CoverageLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("GlyphRecord", "Count", CoverageLayout.HeaderLength, CoverageLayout.GlyphStride,
    WhenVersions = new uint[] { 1 })]
[BinaryElement("GlyphRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryArray("RangeRecord", "Count", CoverageLayout.HeaderLength, CoverageLayout.RangeStride,
    WhenVersions = new uint[] { 2 })]
[BinaryElement("RangeRecord", "StartGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("RangeRecord", "EndGlyph", WireType.UInt16, CoverageLayout.RangeEndOffset, DataField.GlyphId)]
[BinaryElement("RangeRecord", "StartCoverageIndex", WireType.UInt16, CoverageLayout.RangeIndexOffset, DataField.Payload)]
public readonly ref partial struct CoverageView { }
