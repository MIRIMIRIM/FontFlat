using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class AlternateSetLayout
{
    internal const int HeaderLength = 2, CountAt = 0, AlternateStride = 2;
}

// One AlternateSubst alternative set (GSUB lookup type 3). The alternates are
// functionally equivalent glyphs in arbitrary order, so no ordering or uniqueness
// relation is declared here. glyphCount=0 is the card-adjudicated legal no-op set
// (OQ-T4-03-2; corpus has zero witnesses, R7) - same boundary family as the empty
// MultipleSubst Sequence.
[BinaryTable(GsubLayout.Tag, AlternateSetLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("GlyphCount", WireType.UInt16, AlternateSetLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("AlternateRecord", "GlyphCount", AlternateSetLayout.HeaderLength,
    AlternateSetLayout.AlternateStride)]
[BinaryElement("AlternateRecord", "AlternateGlyphId", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct AlternateSetView { }
