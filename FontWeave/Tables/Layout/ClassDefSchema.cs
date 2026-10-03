using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ClassDefLayout
{
    internal const int MinimumHeader = 2, HeaderRecordOffset = 2;
    internal const int DenseHeaderStride = 4, SparseHeaderStride = 2;
    internal const int DenseCountOffset = 4, DenseValuesOffset = 6, SparseRangesOffset = 4;
    internal const int ValueStride = 2, RangeStride = 6, RangeEndOffset = 2, RangeClassOffset = 4;
}

[BinaryTable(0, ClassDefLayout.MinimumHeader, VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryRecord("DenseHeader", ClassDefLayout.DenseHeaderStride)]
[BinaryElement("DenseHeader", "StartGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("DenseHeader", "GlyphCount", WireType.UInt16, 2, DataField.Count)]
[BinaryRecord("SparseHeader", ClassDefLayout.SparseHeaderStride)]
[BinaryElement("SparseHeader", "RangeCount", WireType.UInt16, 0, DataField.Count)]
[BinaryRecord("ClassValue", ClassDefLayout.ValueStride)]
[BinaryElement("ClassValue", "Value", WireType.UInt16, 0, DataField.Payload)]
[BinaryRecord("ClassRange", ClassDefLayout.RangeStride)]
[BinaryElement("ClassRange", "StartGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("ClassRange", "EndGlyph", WireType.UInt16, ClassDefLayout.RangeEndOffset, DataField.GlyphId)]
[BinaryElement("ClassRange", "ClassValue", WireType.UInt16, ClassDefLayout.RangeClassOffset, DataField.Payload)]
public readonly ref partial struct ClassDefView { }
