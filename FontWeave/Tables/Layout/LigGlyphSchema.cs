using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigGlyphLayout
{
    internal const int HeaderLength = 2, CountAt = 0, CaretStride = 2;
}

// One ligated glyph's caret positions: one CaretValue offset per component beyond the
// first, each Offset16 from the LigGlyph start (not from the enclosing LigCaretList).
// The "component count minus one" relation lives in the GSUB ligature domain; GDEF
// carries the declared list without cross-checking it (T4-02 boundary registration).
[BinaryTable(GdefLayout.Tag, LigGlyphLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CaretCount", WireType.UInt16, LigGlyphLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryDerived("CaretsEnd", Constant = LigGlyphLayout.HeaderLength,
    Count = "CaretCount", Stride = LigGlyphLayout.CaretStride)]
[BinaryArray("CaretValueOffsetRecord", "CaretCount", LigGlyphLayout.HeaderLength,
    LigGlyphLayout.CaretStride)]
[BinaryElement("CaretValueOffsetRecord", "CaretValueOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("CaretValue", "CaretValueOffset", typeof(CaretValueView),
    Array = "CaretValueOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "CaretsEnd")]
public readonly ref partial struct LigGlyphView { }
