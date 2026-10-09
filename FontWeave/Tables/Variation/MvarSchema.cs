using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class MvarLayout
{
    internal const uint Tag = 0x4D564152;
    internal const int HeaderLength = 12, ReservedAt = 4, ValueRecordSizeAt = 6,
        ValueRecordCountAt = 8, VariationStoreAt = 10, RecordsAt = 12;
    // The 1.0 ValueRecord is tag + deltaSetOuterIndex + deltaSetInnerIndex
    // (spec mvar.md L71-73); valueRecordSize is the per-record stride declaration
    // and 8 is its 1.0 corpus constant, not a spec constant.
    internal const int ValueRecordSize10 = 8;
}

// MVAR 1.0 (the font-wide metric variations): a 12-byte header whose field order
// follows the spec snapshot (briefing fix 1) - the valueRecordCount word precedes
// the itemVariationStoreOffset (the HVAR/VVAR ordering is reversed here), and the
// store offset is an Offset16, so the write side carries a 64K bound the Offset32
// tables do not (OQ-T5-04-7; the native bounds guard the read side). The count/
// offset coupling ("if the valueRecordCount is zero, set this offset to zero; if
// it is greater than zero, this offset must be greater than zero", spec L60) is
// bidirectional and stays in the hand-written partial.
//
// The valueRecords array is deliberately NOT declared (OQ-T5-04-1 lean): the spec
// is explicit that "implementations must use the valueRecordSize field to
// determine the start of each record" (mvar.md L63) - the stride is a declared
// dynamic value with ">0" as its whole legal domain, and the fvar instanceSize
// precedent walks such a block by hand. The fixed-8 declaration alternative (a
// BinaryArray with stride 8 plus a valueRecordSize==8 hard gate) was pre-checked
// against the generated surface at commit time and rejected: it would narrow the
// spec's ">0" domain into an out-of-spec constraint no corpus host needs.
//
// The reserved word is pinned raw here and hard-refused at validation ("Not
// used; set to 0", spec L57; OQ-T5-04-2). valueTag stays an opaque u32 key in
// strict binary order (OQ-T5-04-3): the 39-tag registry, its case rules and the
// private-tag conventions are the consumption domain and are never interpreted
// here (card 11).
[BinaryTable(MvarLayout.Tag, MvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { 0x00010000 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, MvarLayout.ReservedAt, DataField.Payload)]
[BinaryScalar("ValueRecordSize", WireType.UInt16, MvarLayout.ValueRecordSizeAt,
    DataField.Length)]
[BinaryScalar("ValueRecordCount", WireType.UInt16, MvarLayout.ValueRecordCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("ItemVariationStoreOffset", WireType.UInt16, MvarLayout.VariationStoreAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("HeaderEnd", Constant = MvarLayout.HeaderLength)]
[BinaryReference("ItemVariationStore", "ItemVariationStoreOffset", typeof(ItemVariationStoreView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
public readonly ref partial struct MvarView { }
