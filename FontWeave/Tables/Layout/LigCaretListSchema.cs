using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigCaretListLayout
{
    internal const int HeaderLength = 4, CoverageOffsetAt = 0, LigGlyphCountAt = 2, OffsetStride = 2;
}

// LigCaretList: one Coverage plus one LigGlyph offset per covered ligated glyph, in
// Coverage index order. An empty list (zero count with an empty format-1 Coverage) is a
// legal shape and round-trips unchanged (STIX Two Math witness, OQ-T4-02-6).
[BinaryTable(GdefLayout.Tag, LigCaretListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("CoverageOffset", WireType.UInt16, LigCaretListLayout.CoverageOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("LigGlyphCount", WireType.UInt16, LigCaretListLayout.LigGlyphCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryDerived("LigGlyphsEnd", Constant = LigCaretListLayout.HeaderLength,
    Count = "LigGlyphCount", Stride = LigCaretListLayout.OffsetStride)]
[BinaryArray("LigGlyphOffsetRecord", "LigGlyphCount", LigCaretListLayout.HeaderLength,
    LigCaretListLayout.OffsetStride)]
[BinaryElement("LigGlyphOffsetRecord", "LigGlyphOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "LigGlyphsEnd")]
[BinaryReference("LigGlyph", "LigGlyphOffset", typeof(LigGlyphView),
    Array = "LigGlyphOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "LigGlyphsEnd")]
public readonly ref partial struct LigCaretListView { }
