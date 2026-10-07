using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ExtensionSubstLayout
{
    internal const int HeaderLength = 8, FormatAt = 0, TypeAt = 2, OffsetAt = 4;
}

// GSUB lookup type 7 (ExtensionSubst format 1, the only format): the finite
// extension-to-direct mechanism. The seven explicit branches unwrap exactly one level
// into the typed direct leaves (wrapped types 1..4 since T4-03; 5/6/8 since T4-04 -
// spec forbids only the wrapped=7 self-reference, OQ-T4-04-10). The real witness
// Source Han Sans SC wraps only type 1, offsets up to 110350 - hence the Offset32
// field. No RoleType: the GSUB/GPOS fork lives on the Lookup branch axis, and GPOS
// type 9 is a different structure (T4-06, OQ-T4-03-8). ExtensionOffset is
// ZeroAllowed=false with the declared ExtensionBodyEnd(8) floor: offset 0 would
// re-read this header as the child header (self-reference in disguise), so the zero
// policy refuses it natively (W8's frozen negative, OQ-T4-03-3) and any target inside
// the first 8 bytes refuses as storage-before-body. wrapped=7 (nesting/self-reference)
// and unknown values fall to the DefaultRaw remainder set, where the semantic layer's
// explicit typed read refuses (W7's layered refusal; whole-table reads degrade that
// lookup to metadata-only, OQ-T4-03-4).
[BinaryTable(GsubLayout.Tag, ExtensionSubstLayout.HeaderLength, ContractVersion = 1,
    VersionField = "SubstFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("SubstFormat", WireType.UInt16, ExtensionSubstLayout.FormatAt, DataField.Version)]
[BinaryScalar("ExtensionLookupType", WireType.UInt16, ExtensionSubstLayout.TypeAt, DataField.Tag)]
[BinaryScalar("ExtensionOffset", WireType.UInt32, ExtensionSubstLayout.OffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("ExtensionBodyEnd", Constant = 8)]
[BinaryReference("WrappedSingle", "ExtensionOffset", typeof(SingleSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 1 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedMultiple", "ExtensionOffset", typeof(MultipleSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 2 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedAlternate", "ExtensionOffset", typeof(AlternateSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 3 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedLigature", "ExtensionOffset", typeof(LigatureSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 4 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedContextual", "ExtensionOffset", typeof(ContextualSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 5 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedChaining", "ExtensionOffset", typeof(ChainingSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 6 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedReverse", "ExtensionOffset", typeof(ReverseChainSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 8 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedUnknown", "ExtensionOffset",
    Selector = "ExtensionLookupType", DefaultRaw = true, Scan = ReferenceScan.Observe)]
public readonly ref partial struct ExtensionSubstView { }
