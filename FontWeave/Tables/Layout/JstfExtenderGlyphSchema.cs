using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfExtenderGlyphLayout
{
    internal const int HeaderLength = 2, CountAt = 0, GlyphsAt = 2, GlyphStride = 2;
}

// The ExtenderGlyph list: a count plus a u16 glyph-id array. Version-less declaration
// (the AttachPoint family shape); the GID domain and the increasing order are the
// semantic partial's faces (the GID hard check against the caller's numGlyphs, the
// order an OQ-2 observation), not declaration faces. The Jstf-prefixed name keeps the
// public leaf surface unambiguous (OQ-1's naming note).
[BinaryTable(JstfLayout.Tag, JstfExtenderGlyphLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("GlyphCount", WireType.UInt16, JstfExtenderGlyphLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("ExtenderGlyph", "GlyphCount", JstfExtenderGlyphLayout.GlyphsAt,
    JstfExtenderGlyphLayout.GlyphStride)]
[BinaryElement("ExtenderGlyph", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct JstfExtenderGlyphView { }
