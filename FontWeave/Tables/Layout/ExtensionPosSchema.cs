using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class ExtensionPosLayout
{
    internal const int HeaderLength = 8, FormatAt = 0, TypeAt = 2, OffsetAt = 4;
}

// GPOS lookup type 9 (Extension Positioning format 1, the only format): the same
// finite extension-to-direct mechanism as the GSUB type 7, under the GPOS family tag
// (the GSUB/GPOS fork lives on the Lookup branch axis, the ExtensionSubstLayout
// precedent). The eight explicit branches unwrap exactly one level into the typed
// GPOS leaves - direct 1/2/3 since T4-05 and the mark family 4/5/6 plus the shared
// contextual pair 7/8 since T4-06 (the contextual views are the GSUB/GPOS-common
// structures, OQ-T4-06-3's reuse; the family-tag naming follows the reference).
// Spec forbids only wrapped=9 (the self-reference): it and unknown values fall to
// the DefaultRaw remainder set, whose explicit typed read refuses (the W7 layered
// refusal shape) and whole-table reads degrade that lookup to metadata-only
// (OQ-T4-03-4 discipline). ExtensionOffset is ZeroAllowed=false with the declared
// ExtensionBodyEnd(8) floor: offset 0 would re-read this header as the child header
// (self-reference in disguise), so the zero policy refuses it natively and any
// target inside the first 8 bytes refuses as storage-before-body. Offsets reach
// 110350+ in real witnesses (SS3's wrapped pairs), hence the Offset32 field.
[BinaryTable(GposLayout.Tag, ExtensionPosLayout.HeaderLength, ContractVersion = 1,
    VersionField = "PosFormat", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("PosFormat", WireType.UInt16, ExtensionPosLayout.FormatAt, DataField.Version)]
[BinaryScalar("ExtensionLookupType", WireType.UInt16, ExtensionPosLayout.TypeAt, DataField.Tag)]
[BinaryScalar("ExtensionOffset", WireType.UInt32, ExtensionPosLayout.OffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("ExtensionBodyEnd", Constant = 8)]
[BinaryReference("WrappedSinglePos", "ExtensionOffset", typeof(SinglePosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 1 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedPairPos", "ExtensionOffset", typeof(PairPosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 2 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedCursivePos", "ExtensionOffset", typeof(CursivePosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 3 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedMarkBasePos", "ExtensionOffset", typeof(MarkBasePosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 4 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedMarkLigPos", "ExtensionOffset", typeof(MarkLigPosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 5 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedMarkMarkPos", "ExtensionOffset", typeof(MarkMarkPosView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 6 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
// The GPOS contextual pair shares the GSUB-common contextual views (spec: "formats
// ... common to the GSUB and GPOS tables"; OQ-T4-06-3's single-view resolution).
[BinaryReference("WrappedContextPos", "ExtensionOffset", typeof(ContextualSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 7 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedChainedContextPos", "ExtensionOffset", typeof(ChainingSubstView),
    Selector = "ExtensionLookupType", SelectorValues = new uint[] { 8 },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "ExtensionBodyEnd")]
[BinaryReference("WrappedUnknown", "ExtensionOffset",
    Selector = "ExtensionLookupType", DefaultRaw = true, Scan = ReferenceScan.Observe)]
public readonly ref partial struct ExtensionPosView { }
