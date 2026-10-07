using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ContextualSubstLayout
{
    // The bound header is 6 bytes (format + one word at 2 + one word at 4); every
    // format-specific tail word beyond it is a branched declaration.
    internal const int MinimumHeader = 6;
    internal const int GlyphCountField = 2, ClassSeqRuleSetCountField = 6;
    internal const int Format1ArrayAt = 6, Format2ArrayAt = 8, OffsetStride = 2;
}

/// <summary>The calling direction a shared contextual rule-set header is opened under
/// (OQ-T4-04-2): the four rule-set layouts of types 5/6 are wire-identical (a rule
/// count plus its Offset16 rule-offset array), so one view serves them and the role
/// selects which rule body the semantic partial walks. It is never a wire field.</summary>
internal enum ContextualRuleSetRole : byte
{
    Sequence = 1,
    ClassSequence = 2,
    ChainedSequence = 3,
    ChainedClassSequence = 4,
}

internal static class ContextualRuleSetLayout
{
    internal const int HeaderLength = 2, OffsetStride = 2;
}

// One shared structural definition for SequenceRuleSet, ClassSequenceRuleSet,
// ChainedSequenceRuleSet and ChainedClassSequenceRuleSet (the LayoutTagListRole
// precedent). The rule bodies behind the offsets are the card's hand-written
// boundary - variable-length rule/record walking has no declaration form (OQ-5) -
// so the offsets stay raw counted records and the semantic partial owns their
// bounds and body walks. A rule offset of zero is refused at the generated getter
// (no spec-defined NULL rule).
[BinaryTable(GsubLayout.Tag, ContextualRuleSetLayout.HeaderLength, ContractVersion = 1,
    RoleType = typeof(ContextualRuleSetRole),
    SupportedRoles = new long[] { (long)ContextualRuleSetRole.Sequence,
        (long)ContextualRuleSetRole.ClassSequence, (long)ContextualRuleSetRole.ChainedSequence,
        (long)ContextualRuleSetRole.ChainedClassSequence })]
[BinaryScalar("RuleCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("RuleOffsetRecord", "RuleCount", ContextualRuleSetLayout.HeaderLength,
    ContextualRuleSetLayout.OffsetStride)]
[BinaryElement("RuleOffsetRecord", "RuleOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 2, Count = "RuleCount", Stride = ContextualRuleSetLayout.OffsetStride)]
public readonly ref partial struct ContextualRuleSetView { }

// GSUB lookup type 5 (ContextualSubst), all three formats one view (the PairPos
// per-format precedent, OQ-T4-04-1). The three formats reuse every header word for a
// different field, so each word is declared once per interpretation as a
// version-branched scalar (NameSchema's conditional-field form); outside its version
// a branched scalar reads its Otherwise value (zero), which keeps the three counted
// arrays - each keyed on one format's count - empty everywhere else. Format 3 is
// count-first (glyphCount before seqLookupCount, the frozen chapter2 erratum). The
// seqLookupRecords and every rule body stay the semantic partial's hand-walked
// boundary (OQ-5); NULL rule-set offsets are spec-defined legal entries (205 real
// witnesses, R7) and open as present=false through the Optional null policy.
[BinaryTable(GsubLayout.Tag, ContextualSubstLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("SubstFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 1)]
[BinaryScalar("ClassCoverageOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("GlyphCount", WireType.UInt16, 2, DataField.Count,
    Semantic = NumberSemantic.Count, WhenVersion = 3)]
[BinaryScalar("SequenceRuleSetCount", WireType.UInt16, 4, DataField.Count,
    Semantic = NumberSemantic.Count, WhenVersion = 1)]
[BinaryScalar("ClassDefOffset", WireType.UInt16, 4, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, WhenVersion = 2)]
[BinaryScalar("SeqLookupCount", WireType.UInt16, 4, DataField.Count,
    Semantic = NumberSemantic.Count, WhenVersion = 3)]
[BinaryScalar("ClassSeqRuleSetCount", WireType.UInt16, ContextualSubstLayout.ClassSeqRuleSetCountField,
    DataField.Count, Semantic = NumberSemantic.Count, WhenVersion = 2)]
// The declared storage floors: 6 + 2n (fmt1), 8 + 2n (fmt2) and 6 + 2*glyphCount
// (fmt3, the coverage-offset array end; the records after it are hand-walked).
[BinaryDerived("Format1BodyEnd", Constant = ContextualSubstLayout.Format1ArrayAt,
    Count = "SequenceRuleSetCount", Stride = ContextualSubstLayout.OffsetStride,
    WhenVersion = 1, Otherwise = "Format2BodyEnd")]
[BinaryDerived("Format2BodyEnd", Constant = ContextualSubstLayout.Format2ArrayAt,
    Count = "ClassSeqRuleSetCount", Stride = ContextualSubstLayout.OffsetStride,
    WhenVersion = 2, Otherwise = "Format3BodyEnd")]
// The chain's unconditional terminator: the fmt3 coverage-array end 6 + 2*glyphCount.
// Outside format 3 the branched glyph count reads its zero fallback, so the value
// degrades to the constant harmlessly (the references keyed on the other formats
// never evaluate it).
[BinaryDerived("Format3BodyEnd", Constant = ContextualSubstLayout.Format1ArrayAt,
    Count = "GlyphCount", Stride = ContextualSubstLayout.OffsetStride)]
[BinaryArray("SeqRuleSetOffsetRecord", "SequenceRuleSetCount", ContextualSubstLayout.Format1ArrayAt,
    ContextualSubstLayout.OffsetStride)]
[BinaryElement("SeqRuleSetOffsetRecord", "RuleSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryArray("ClassRuleSetOffsetRecord", "ClassSeqRuleSetCount", ContextualSubstLayout.Format2ArrayAt,
    ContextualSubstLayout.OffsetStride)]
[BinaryElement("ClassRuleSetOffsetRecord", "RuleSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryArray("Format3CoverageOffsetRecord", "GlyphCount", ContextualSubstLayout.Format1ArrayAt,
    ContextualSubstLayout.OffsetStride)]
[BinaryElement("Format3CoverageOffsetRecord", "CoverageOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format1BodyEnd", WhenVersions = new uint[] { 1 })]
[BinaryReference("ClassCoverage", "ClassCoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("ClassDef", "ClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("Format3Coverage", "CoverageOffset", typeof(CoverageView),
    Array = "Format3CoverageOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "Format3BodyEnd",
    WhenVersions = new uint[] { 3 })]
[BinaryReference("SequenceRuleSet", "RuleSetOffset", typeof(ContextualRuleSetView),
    Array = "SeqRuleSetOffsetRecord", TargetRole = (long)ContextualRuleSetRole.Sequence,
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "Format1BodyEnd", WhenVersions = new uint[] { 1 })]
[BinaryReference("ClassSequenceRuleSet", "RuleSetOffset", typeof(ContextualRuleSetView),
    Array = "ClassRuleSetOffsetRecord", TargetRole = (long)ContextualRuleSetRole.ClassSequence,
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "Format2BodyEnd", WhenVersions = new uint[] { 2 })]
public readonly ref partial struct ContextualSubstView { }
