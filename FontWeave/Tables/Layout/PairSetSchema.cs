using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class PairSetLayout
{
    internal const uint Tag = 0x47504F53;
    internal const int MinimumHeader = 2, CountField = 0, WordStride = 2;
}

// All pairs beginning with one covered glyph (GPOS lookup type 2, format 1's second
// level). The PairValue record stride depends on the owning PairPos subtable's two
// value formats (2 + 2*popcount(vf1) + 2*popcount(vf2) bytes), so the record body is
// the card's hand-written boundary: the second glyph and each present word are 2-byte
// records read at semantic-computed positions (SinglePos same-owner precedent). The
// second-glyph ordering relation is the semantic chain's property (OQ-T4-05-1).
[BinaryTable(PairSetLayout.Tag, PairSetLayout.MinimumHeader, ContractVersion = 1)]
[BinaryScalar("PairValueCount", WireType.UInt16, PairSetLayout.CountField, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryRecord("PairSecondGlyph", PairSetLayout.WordStride)]
[BinaryElement("PairSecondGlyph", "SecondGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryRecord("PairScalar", PairSetLayout.WordStride)]
[BinaryElement("PairScalar", "Value", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("PairDeviceOffset", PairSetLayout.WordStride)]
[BinaryElement("PairDeviceOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct PairSetView { }
