using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathGlyphConstructionLayout
{
    internal const int HeaderLength = 4, GlyphAssemblyOffsetAt = 0, VariantCountAt = 2,
        VariantRecordsAt = 4;
}

// MathGlyphConstruction (T4-10 section 2): the Optional/ZeroAllowed assembly slot (the
// spec's explicit "May be NULL"; Partial3's assembly-less zero-variant construction is
// the OQ-7 legal positive) over the counted MathGlyphVariantRecord array. The variant
// glyph ids are GlyphId-typed words whose numGlyphs domain is the semantic layer's hard
// check (the JstfExtenderGlyph declaration shape); the array order is the coverage
// order's semantic twin and is never re-sorted.
[BinaryTable(MathLayout.Tag, MathGlyphConstructionLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("GlyphAssemblyOffset", WireType.UInt16, MathGlyphConstructionLayout.GlyphAssemblyOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("VariantCount", WireType.UInt16, MathGlyphConstructionLayout.VariantCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("MathGlyphVariantRecord", "VariantCount", MathGlyphConstructionLayout.VariantRecordsAt,
    MathLayout.GlyphVariantRecordSize)]
[BinaryElement("MathGlyphVariantRecord", "VariantGlyph", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("MathGlyphVariantRecord", "AdvanceMeasurement", WireType.UInt16, 2, DataField.Payload)]
[BinaryDerived("RecordsEnd", Constant = MathGlyphConstructionLayout.VariantRecordsAt,
    Count = "VariantCount", Stride = MathLayout.GlyphVariantRecordSize)]
[BinaryReference("GlyphAssembly", "GlyphAssemblyOffset", typeof(MathGlyphAssemblyView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MathGlyphConstructionView { }
