using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigatureLayout
{
    internal const int HeaderLength = 4, LigatureGlyphAt = 0, ComponentCountAt = 2, ComponentStride = 2;
}

// One ligature rule (GSUB lookup type 4 leaf): the output ligature glyph plus the
// SECOND component onwards. componentCount includes the first component, which the
// owning LigatureSet's Coverage entry carries, so the wire array is the declared
// rawCount-1 (CountSubtract) form - the first real consumer of the A2 contract
// (OQ-A2-8 closed here). componentCount=0 therefore underflows declaratively in the
// generated accessor (GC7: InvalidFormat/InvalidValue at the raw count field,
// Detail=0/Expected=1 - W6's frozen negative), while componentCount=1 is the legal
// zero-component single-component ligature (OQ-T4-03-2; corpus witnesses span 2..6).
[BinaryTable(GsubLayout.Tag, LigatureLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("LigatureGlyph", WireType.UInt16, LigatureLayout.LigatureGlyphAt, DataField.GlyphId)]
[BinaryScalar("ComponentCount", WireType.UInt16, LigatureLayout.ComponentCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryDerived("ComponentRecordCount", Count = "ComponentCount", CountSubtract = 1,
    Semantic = NumberSemantic.Count)]
[BinaryArray("ComponentRecord", "ComponentRecordCount", LigatureLayout.HeaderLength,
    LigatureLayout.ComponentStride)]
[BinaryElement("ComponentRecord", "ComponentGlyphId", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct LigatureView { }
