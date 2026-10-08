using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernClassTableLayout
{
    internal const int HeaderLength = 4, FirstGlyphAt = 0, GlyphCountAt = 2,
        ClassesAt = 4, ClassStride = 2;
}

// One format 2 class table: firstGlyph plus one class word per covered glyph. The
// class words are premultiplied byte offsets into the owning subtable (right classes
// step by 2, left classes by rowWidth - the jellee byte-level convention, the card's
// OQ-7 derivation), but this view carries them opaquely: the offset semantics belong
// to the matrix walk, not to the class table. Version-less declaration (the
// AttachPoint family shape); the numGlyphs domain of firstGlyph+nGlyphs is the OQ-4
// observation recorded by the owning subtable's semantic partial.
[BinaryTable(KernLayout.Tag, KernClassTableLayout.ClassesAt, ContractVersion = 1)]
[BinaryScalar("FirstGlyph", WireType.UInt16, KernClassTableLayout.FirstGlyphAt, DataField.GlyphId)]
[BinaryScalar("GlyphCount", WireType.UInt16, KernClassTableLayout.GlyphCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryDerived("ClassesEnd", Constant = KernClassTableLayout.ClassesAt,
    Count = "GlyphCount", Stride = KernClassTableLayout.ClassStride)]
[BinaryArray("Class", "GlyphCount", KernClassTableLayout.ClassesAt,
    KernClassTableLayout.ClassStride)]
[BinaryElement("Class", "Value", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct KernClassTableView { }
