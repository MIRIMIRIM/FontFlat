using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class SequenceLayout
{
    internal const int HeaderLength = 2, CountAt = 0, SubstituteStride = 2;
}

// One MultipleSubst output sequence (GSUB lookup type 2). glyphCount=0 is the
// card-adjudicated legal empty sequence (OQ-T4-03-2, W9 positive pin): the spec text
// demands glyphCount > 0, but the frozen corpus has zero witnesses either way (R7) and
// the adopted contract treats the empty body as a structural no-op rather than a
// refusal, so the negative family only pins the spec/contract-refused shapes.
[BinaryTable(GsubLayout.Tag, SequenceLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("GlyphCount", WireType.UInt16, SequenceLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("SubstituteRecord", "GlyphCount", SequenceLayout.HeaderLength, SequenceLayout.SubstituteStride)]
[BinaryElement("SubstituteRecord", "SubstituteGlyphId", WireType.UInt16, 0, DataField.GlyphId)]
public readonly ref partial struct SequenceView { }
