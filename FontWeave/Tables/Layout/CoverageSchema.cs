using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class CoverageLayout
{
    internal const int HeaderLength = 4, CountOffset = 2;
    internal const int GlyphStride = 2, RangeStride = 6;
    internal const int RangeEndOffset = 2, RangeIndexOffset = 4;
}

[BinaryTable(0, CoverageLayout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Count", WireType.UInt16, CoverageLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("GlyphRecord", CoverageLayout.GlyphStride)]
[BinaryElement("GlyphRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryRecord("RangeRecord", CoverageLayout.RangeStride)]
[BinaryElement("RangeRecord", "StartGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("RangeRecord", "EndGlyph", WireType.UInt16, CoverageLayout.RangeEndOffset, DataField.GlyphId)]
[BinaryElement("RangeRecord", "StartCoverageIndex", WireType.UInt16, CoverageLayout.RangeIndexOffset, DataField.Payload)]
public readonly ref partial struct CoverageView { }
