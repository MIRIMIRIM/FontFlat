using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathVariantsLayout
{
    internal const int HeaderLength = 10, MinConnectorOverlapAt = 0,
        VertGlyphCoverageOffsetAt = 2, HorizGlyphCoverageOffsetAt = 4,
        VertGlyphCountAt = 6, HorizGlyphCountAt = 8, ConstructionOffsetsAt = 10,
        ConstructionOffsetStride = 2;
}

// MathVariants (T4-10 section 2, OQ-1's lean ruling): the UFWORD minConnectorOverlap,
// the two Optional/ZeroAllowed coverage pointers (count=0 with a NULL coverage and
// count=0 with a present-empty coverage are both real wire forms - Partial1 and the
// 8d9c4b19 positive), the two counts and the two counted Offset16 arrays whose items
// are Required per-item (no spec NULL note, zero NULL instances across the 19-host
// matrix; a zero count is the legal empty array). The array order IS the semantic
// coverage order (the spec's "must be arranged in coverage order" is a semantics
// declaration, never re-sorted); the count==coverage coincidence on both directions
// is the OQ-6 hard face at the semantic layer. The VertConstructionOffsetsEnd chain
// places the horizontal array after the vertical one and RecordsEnd floors every
// coverage and construction edge strictly forward.
[BinaryTable(MathLayout.Tag, MathVariantsLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("MinConnectorOverlap", WireType.UInt16, MathVariantsLayout.MinConnectorOverlapAt, DataField.Payload)]
[BinaryScalar("VertGlyphCoverageOffset", WireType.UInt16, MathVariantsLayout.VertGlyphCoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("HorizGlyphCoverageOffset", WireType.UInt16, MathVariantsLayout.HorizGlyphCoverageOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("VertGlyphCount", WireType.UInt16, MathVariantsLayout.VertGlyphCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("HorizGlyphCount", WireType.UInt16, MathVariantsLayout.HorizGlyphCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("VertGlyphConstructionOffset", "VertGlyphCount", MathVariantsLayout.ConstructionOffsetsAt,
    MathVariantsLayout.ConstructionOffsetStride)]
[BinaryElement("VertGlyphConstructionOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("VertConstructionOffsetsEnd", Constant = MathVariantsLayout.ConstructionOffsetsAt,
    Count = "VertGlyphCount", Stride = MathVariantsLayout.ConstructionOffsetStride)]
[BinaryArray("HorizGlyphConstructionOffset", "HorizGlyphCount", 0, MathVariantsLayout.ConstructionOffsetStride,
    Start = "VertConstructionOffsetsEnd")]
[BinaryElement("HorizGlyphConstructionOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Base = "VertConstructionOffsetsEnd",
    Count = "HorizGlyphCount", Stride = MathVariantsLayout.ConstructionOffsetStride)]
[BinaryReference("VertGlyphCoverage", "VertGlyphCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("HorizGlyphCoverage", "HorizGlyphCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("VertGlyphConstruction", "Offset", typeof(MathGlyphConstructionView), Array = "VertGlyphConstructionOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
[BinaryReference("HorizGlyphConstruction", "Offset", typeof(MathGlyphConstructionView), Array = "HorizGlyphConstructionOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct MathVariantsView { }
