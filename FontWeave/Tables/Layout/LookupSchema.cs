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
// property, so the scan only proves the header binding; no Follow - SingleSubst and
// SinglePos have not enabled scan, and a Follow declaration would be FWG005.
[BinaryReference("SingleSubst", "SubtableOffset", typeof(SingleSubstView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gsub },
    Selector = "LookupType", SelectorValues = new uint[] { 1 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
[BinaryReference("SinglePos", "SubtableOffset", typeof(SinglePosView),
    Array = "SubtableOffsetRecord", WhenRoles = new long[] { (long)LayoutLookupKind.Gpos },
    Selector = "LookupType", SelectorValues = new uint[] { 1 }, Minimum = "SubtablePrefix",
    Scan = ReferenceScan.OpenOnly)]
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
