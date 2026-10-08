using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class GvarLayout
{
    internal const uint Tag = 0x67766172;
    internal const int HeaderLength = 20;
    internal const int AxisCountAt = 4, SharedTupleCountAt = 6, SharedTuplesOffsetAt = 8,
        GlyphCountAt = 12, FlagsAt = 14, GlyphVariationDataArrayOffsetAt = 16;
    // gvar 1.0 only: the version is two u16 fields (major=1, minor=0) read as one
    // packed u32 - the byte-identical HvarView reading (card 0, briefing fix 1).
    // There is no 1.1 (222/222 repository hosts are 1.0).
    internal const uint Version10 = 0x00010000;
    // The only defined header flag bit (spec gvar.md): bit0 clear = the
    // GlyphVariationData offsets are uint16, set = uint32. No IUP bit exists
    // (briefing fix 2); the upper 15 bits carry no defined semantics and are not
    // checked (the card's hard-check list pins the tvc/tupleIndex reserved masks
    // only - the corpus shows all-zero upper flags across the 222-host scan).
    internal const ushort LongOffsetsFlag = 0x0001;
}

// gvar 1.0 (the glyph variations): the declarable skeleton is exactly the eight
// header scalars. Everything past the header is outside the V1 declaration
// domain and stays hand-written in the semantic partial (card 2/7, OQ-2 lean):
// the offsets array carries a flags-conditional element width (bit0) with a /2
// short-form scale and a derived GlyphCount+1 count - the array attributes take a
// constant stride and an unconditional element scale, so neither the width
// selection nor the /2 decode can be expressed; the shared tuples and the
// GlyphVariationData blocks are dynamic-stride/serialized walks (the fvar
// instance / DeltaSetIndexMap packed-entry precedents). No child reference is
// declared (no "open at a computed window" capability exists and the card's
// frozen shape does not extend the contract), so there is no structure scan.
[BinaryTable(GvarLayout.Tag, GvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { GvarLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("AxisCount", WireType.UInt16, GvarLayout.AxisCountAt, DataField.Count)]
[BinaryScalar("SharedTupleCount", WireType.UInt16, GvarLayout.SharedTupleCountAt, DataField.Count)]
[BinaryScalar("SharedTuplesOffset", WireType.UInt32, GvarLayout.SharedTuplesOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("GlyphCount", WireType.UInt16, GvarLayout.GlyphCountAt, DataField.Count)]
[BinaryScalar("Flags", WireType.UInt16, GvarLayout.FlagsAt, DataField.Flags)]
[BinaryScalar("GlyphVariationDataArrayOffset", WireType.UInt32,
    GvarLayout.GlyphVariationDataArrayOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct GvarView { }
