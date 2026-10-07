using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LookupLayout
{
    internal const int HeaderLength = 6, TypeAt = 0, FlagsAt = 2, CountAt = 4, OffsetStride = 2;
    internal const ushort IgnoreMask = 0x000E, IgnoreMarks = 0x0008,
        UseMarkFilteringSet = 0x0010, ReservedMask = 0x00E0, AttachmentMask = 0xFF00;
}

[BinaryTable(0, LookupLayout.HeaderLength, ContractVersion = 1, GenerateStructureScan = true,
    RoleType = typeof(LayoutLookupKind),
    SupportedRoles = new long[] { (long)LayoutLookupKind.Gsub, (long)LayoutLookupKind.Gpos })]
[BinaryScalar("LookupType", WireType.UInt16, LookupLayout.TypeAt, DataField.Tag)]
[BinaryScalar("Flags", WireType.UInt16, LookupLayout.FlagsAt, DataField.Flags)]
[BinaryScalar("SubtableCount", WireType.UInt16, LookupLayout.CountAt, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("SubtableOffsetRecord", "SubtableCount", LookupLayout.HeaderLength, LookupLayout.OffsetStride)]
[BinaryElement("SubtableOffsetRecord", "SubtableOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryRecord("FilterWord", 2)]
[BinaryElement("FilterWord", "MarkFilteringSet", WireType.UInt16, 0, DataField.Count)]
[BinaryComputed("SubtablePrefix", "SubtablePrefixOf",
    Inputs = new[] { "Flags", "SubtableCount" }, Maximum = 131078ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset,
    DiagnosticSource = "SubtableCount")]
// OpenOnly scan policy (OQ-M1c-8): the subtable bodies stay the semantic chain's
// property, so the scan only proves the header binding; no Follow - none of the
// eleven branch targets has enabled scan, and a Follow declaration would be FWG005.
[BinaryReference("SingleSubst", "SubtableOffset", typeof(SingleSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 1 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("SinglePos", "SubtableOffset", typeof(SinglePosView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gpos },
    Selector = "LookupType", SelectorValues = new uint[] { 1 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
// T4-03: the GSUB direct substitution branches (types 2/3/4) and the type-7 extension
// branch share the SingleSubst branch geometry exactly (M1c OQ-M1c-8 reasoning: leaf
// semantics stay in the semantic chain, the scan only proves header binding).
[BinaryReference("MultipleSubst", "SubtableOffset", typeof(MultipleSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 2 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("AlternateSubst", "SubtableOffset", typeof(AlternateSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 3 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("LigatureSubst", "SubtableOffset", typeof(LigatureSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 4 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("ExtensionSubst", "SubtableOffset", typeof(ExtensionSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 7 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
// T4-04: the contextual family (types 5/6/8) joins with the same branch geometry -
// the leaf semantics stay in the semantic chain, the scan only proves header binding.
// Until the typed read lands the kind-aware gate keeps refusing these types, so the
// whole-table walk still degrades them to metadata-only (the OQ-12 flip family).
[BinaryReference("ContextualSubst", "SubtableOffset", typeof(ContextualSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 5 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("ChainingSubst", "SubtableOffset", typeof(ChainingSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 6 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("ReverseChainSubst", "SubtableOffset", typeof(ReverseChainSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 8 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
// T4-05: the GPOS pair/cursive branches share the SinglePos branch geometry exactly
// (OQ-M1c-8 reasoning: leaf semantics stay in the semantic chain, the scan only proves
// header binding). GPOS type 9 extension has no branch here and is never unwrapped -
// its metadata-only boundary is pinned on both the read and write sides (T4-06).
[BinaryReference("PairPos", "SubtableOffset", typeof(PairPosView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gpos },
    Selector = "LookupType", SelectorValues = new uint[] { 2 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("CursivePos", "SubtableOffset", typeof(CursivePosView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gpos },
    Selector = "LookupType", SelectorValues = new uint[] { 3 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
// The remainder set is now a GSUB type outside {1,2,3,4,5,6,7,8} (unknown values
// only - types 5/6/8 have declared branches since T4-04, and until the typed read
// lands the kind-aware gate keeps their metadata-only boundary) plus every GPOS
// value outside {1,2,3} (types 4/5/6/7/8 keep the typed refusal in place, W11's
// pin; type 7 has no GPOS extension branch and type 9 is T4-06's structure,
// OQ-T4-03-8) and unknown values. The raw-observe/typed-refuse layering of unknown
// types is unchanged.
[BinaryReference("UnknownTypeRaw", "SubtableOffset", Array = "SubtableOffsetRecord",
    Selector = "LookupType", DefaultRaw = true, Scan = ReferenceScan.Observe)]
public readonly ref partial struct LookupView
{
    // 6 + 2*count + (useSet ? 2 : 0): the mask test plus conditional addition is one of
    // the explicitly allowed bounded computations (spec 2.3). The worst case
    // 6 + 2*65535 + 2 = 131078 matches the declared static Maximum.
    private static ReadStatus SubtablePrefixOf(ushort flags, ushort subtableCount,
        DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
    {
        value = 6UL + 2UL * subtableCount + ((flags & LookupLayout.UseMarkFilteringSet) != 0 ? 2UL : 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
