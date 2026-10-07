using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ChainingSubstLayout
{
    // The bound header is 6 bytes; the format-specific tails are branched declarations.
    internal const int MinimumHeader = 6;
    internal const int BacktrackGlyphCountField = 2, ClassSeqRuleSetCountField = 10,
        ClassDefOffsetField = 4;
    internal const int Format1ArrayAt = 6, Format2ArrayAt = 12, Format3ArrayAt = 4,
        OffsetStride = 2;
}

// GSUB lookup type 6 (ChainingContextSubst), all three formats one view (OQ-T4-04-1,
// the PairPos per-format precedent with the contextual view's branched-scalar header
// words). Format 2 carries the three class definitions (backtrack/input/lookahead,
// which may legally share one address - the PairPos W12 precedent); format 3 is the
// coverage-array chain whose backtrack offsets are the declared counted array from
// the constant start 4. The input and lookahead coverage arrays sit behind computed
// starts (4+2*backtrack / 4+2*backtrack+2*input) - no declaration form exists for a
// computed array start (OQ-T4-04-5's pre-registered V1 boundary), so they, the
// seqLookup records and every rule body stay the semantic partial's hand-walked
// boundary with hand-written fixup helpers on the write face (the dynamic ValueFormat
// row precedent). NULL rule-set offsets are spec-defined legal entries and open as
// present=false through the Optional null policy.
[BinaryTable(GsubLayout.Tag, ChainingSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("SubstFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 1)]
[BinaryScalar("ClassCoverageOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("BacktrackGlyphCount", WireType.UInt16, ChainingSubstLayout.BacktrackGlyphCountField,
    DataField.Count, Semantic = NumberSemantic.Count, WhenVersion = 3)]
[BinaryScalar("ChainedSeqRuleSetCount", WireType.UInt16, 4, DataField.Count,
    Semantic = NumberSemantic.Count, WhenVersion = 1)]
[BinaryScalar("BacktrackClassDefOffset", WireType.UInt16, ChainingSubstLayout.ClassDefOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("InputClassDefOffset", WireType.UInt16, 6, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("LookaheadClassDefOffset", WireType.UInt16, 8, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("ChainedClassSeqRuleSetCount", WireType.UInt16,
    ChainingSubstLayout.ClassSeqRuleSetCountField, DataField.Count,
    Semantic = NumberSemantic.Count, WhenVersion = 2)]
// The declared storage floors: 6 + 2n (fmt1), 12 + 2n (fmt2) and the fmt3 backtrack
// array end 4 + 2*backtrackGlyphCount (the input chain behind it is hand-walked).
[BinaryDerived("Format1BodyEnd", Constant = ChainingSubstLayout.Format1ArrayAt,
    Count = "ChainedSeqRuleSetCount", Stride = ChainingSubstLayout.OffsetStride,
    WhenVersion = 1, Otherwise = "Format2BodyEnd")]
[BinaryDerived("Format2BodyEnd", Constant = ChainingSubstLayout.Format2ArrayAt,
    Count = "ChainedClassSeqRuleSetCount", Stride = ChainingSubstLayout.OffsetStride,
    WhenVersion = 2, Otherwise = "Format3BodyEnd")]
// The chain's unconditional terminator: the fmt3 backtrack-array end
// 4 + 2*backtrackGlyphCount. Outside format 3 the branched count reads its zero
// fallback, so the value degrades to the constant harmlessly.
[BinaryDerived("Format3BodyEnd", Constant = ChainingSubstLayout.Format3ArrayAt,
    Count = "BacktrackGlyphCount", Stride = ChainingSubstLayout.OffsetStride)]
[BinaryArray("ChainedSeqRuleSetOffsetRecord", "ChainedSeqRuleSetCount",
    ChainingSubstLayout.Format1ArrayAt, ChainingSubstLayout.OffsetStride)]
[BinaryElement("ChainedSeqRuleSetOffsetRecord", "RuleSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryArray("ChainedClassRuleSetOffsetRecord", "ChainedClassSeqRuleSetCount",
    ChainingSubstLayout.Format2ArrayAt, ChainingSubstLayout.OffsetStride)]
[BinaryElement("ChainedClassRuleSetOffsetRecord", "RuleSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryArray("BacktrackCoverageOffsetRecord", "BacktrackGlyphCount",
    ChainingSubstLayout.Format3ArrayAt, ChainingSubstLayout.OffsetStride)]
[BinaryElement("BacktrackCoverageOffsetRecord", "CoverageOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format1BodyEnd", WhenVersions = new uint[] { 1 })]
[BinaryReference("ClassCoverage", "ClassCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
// The three class definitions may legally name one shared ClassDef (spec gpos.md:307
// wording for the chained sibling, W12's frozen shape); identity sharing is the
// semantic cache's property, never assumed from equal addresses.
[BinaryReference("BacktrackClassDef", "BacktrackClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("InputClassDef", "InputClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("LookaheadClassDef", "LookaheadClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("BacktrackCoverage", "CoverageOffset", typeof(CoverageView),
    Array = "BacktrackCoverageOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "Format3BodyEnd",
    WhenVersions = new uint[] { 3 })]
[BinaryReference("ChainedSequenceRuleSet", "RuleSetOffset", typeof(ContextualRuleSetView),
    Array = "ChainedSeqRuleSetOffsetRecord", TargetRole = (long)ContextualRuleSetRole.ChainedSequence,
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "Format1BodyEnd", WhenVersions = new uint[] { 1 })]
[BinaryReference("ChainedClassSequenceRuleSet", "RuleSetOffset", typeof(ContextualRuleSetView),
    Array = "ChainedClassRuleSetOffsetRecord",
    TargetRole = (long)ContextualRuleSetRole.ChainedClassSequence,
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
public readonly ref partial struct ChainingSubstView { }
