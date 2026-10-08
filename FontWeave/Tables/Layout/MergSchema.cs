using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MergLayout
{
    internal const uint Tag = 0x4D455247;
    // The 10-byte header: one u16 version word ("set to 0"), the merge class count and
    // two Offset16 slots, both measured "from the start of the MERG table" (card §0).
    internal const int MinimumHeader = 10;
    internal const int MergeClassCountAt = 2;
    internal const int MergeDataOffsetAt = 4, ClassDefCountAt = 6,
        OffsetToClassDefOffsetsAt = 8;
    // The merge-class square matrix is bounded by the format-design reading (every MERG
    // datum lives inside the table's own Offset16 window): n² ≤ 65536 - 10 = 65526, so
    // n ≤ 255 and the declared computed-count Maximum is 255² = 65025 (card §0 inference;
    // the OQ-5 ruling lands the bound as the early finite refusal, ahead of any scan).
    internal const ulong MergeEntryCountMaximum = 65025;
    // The six defined flag bits (0x77) and the two reserved bits 0x08|0x80 (0x88). The
    // reserved pair carries the spec's imperative "set to 0" and the T0 rejection list
    // names it - the OQ-1 hard face: an occupied reserved bit refuses InvalidValue at
    // the entry's payload byte (unlike MATH partFlags' bare "Reserved" observation).
    internal const byte DefinedFlagsMask = 0x77, ReservedFlagsMask = 0x88;
    internal const byte MergeLtrFlag = 0x01, GroupLtrFlag = 0x02,
        SecondIsSubordinateLtrFlag = 0x04;
    internal const byte MergeRtlFlag = 0x10, GroupRtlFlag = 0x20,
        SecondIsSubordinateRtlFlag = 0x40;
}

// The MERG root (card §2, the seven OQ rulings): version is a plain u16 whose only
// supported value is 0 - not the packed u32 read of the GSUB/GPOS/JSTF/MATH header
// family, and not 1.0 (the first-round briefing's "version 1.0" assertion is retired;
// the normative chapter says "set to 0"). The two Offset16 slots are Required non-zero
// (OQ-6: the chapter carries no NULL note anywhere, so a zero would point back into
// the header and the declared ZeroAllowed=false + the semantic HeaderEnd floor refuse
// it), the classDefOffsets array is positioned by the raw OffsetToClassDefOffsets
// scalar (the T5-02 fvar Start form, second use), and the merge matrix is the flat
// row-major u8 array whose count is the declared computed square MergeEntryCountOf
// over MergeClassCount with the 65025 declared Maximum (OQ-3 lean + OQ-5; the wire's
// "rows × columns" two-level organization is the handwritten partial's semantics, no
// child view exists - the count lives at the root and the matrix owns no count bytes).
// Every ClassDef body is the T4-01 frozen ClassDefView consumed through the per-item
// Required reference with the HeaderEnd floor: MERG declares no class semantics of
// its own (AGENTS' one-formal-View-per-layout rule; there is no MergClassDefView).
[BinaryTable(MergLayout.Tag, MergLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MergeClassCount", WireType.UInt16, 2, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("MergeDataOffset", WireType.UInt16, MergLayout.MergeDataOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("ClassDefCount", WireType.UInt16, MergLayout.ClassDefCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("OffsetToClassDefOffsets", WireType.UInt16,
    MergLayout.OffsetToClassDefOffsetsAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryComputed("MergeEntryCount", "MergeEntryCountOf", Inputs = new[] { "MergeClassCount" },
    Maximum = MergLayout.MergeEntryCountMaximum, Semantic = NumberSemantic.Count,
    DiagnosticField = DataField.Count, DiagnosticSource = "MergeClassCount")]
[BinaryArray("ClassDefOffset", "ClassDefCount", 0, 2,
    Start = "OffsetToClassDefOffsets")]
[BinaryElement("ClassDefOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryArray("MergeEntry", "MergeEntryCount", 0, 1,
    Start = "MergeDataOffset")]
[BinaryElement("MergeEntry", "Flags", WireType.UInt8, 0, DataField.Payload)]
[BinaryDerived("HeaderEnd", Constant = MergLayout.MinimumHeader)]
[BinaryReference("ClassDef", "Offset", typeof(ClassDefView), Array = "ClassDefOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "HeaderEnd")]
public readonly ref partial struct MergView
{
    // The OQ-5 computed-count hook: the merge data is the mergeClassCount² square in
    // row-major wire order, so the derived count is the exact square (no +1 slack -
    // the MathKern KernValueCountOf family, minus the derivation). The declared
    // Maximum 65025 (=255²) refuses mergeClassCount ≥ 256 at the computed count,
    // before any matrix scan (the §0 upper bound as a declaration).
    private static ReadStatus MergeEntryCountOf(ushort mergeClassCount,
        DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
    {
        value = (ulong)mergeClassCount * mergeClassCount;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
