using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathLayout
{
    internal const uint Tag = 0x4D415448;
    // The packed u32 read is the GSUB/GPOS/JSTF header family shape: major/minor are
    // the two u16 halves of one supported version (OT 1.9.1 "currently set to 1.0").
    internal const uint Version10 = 0x00010000;
    // The 10-byte header: one packed version word plus exactly three Offset16 slots
    // (the T4-10 section 0 first correction - there is no fourth or fifth slot).
    internal const int Header10 = 10,
        ConstantsOffsetAt = 4, GlyphInfoOffsetAt = 6, VariantsOffsetAt = 8;
    // MathConstants is the 56-field fixed body (3 int16 + 2 UFWORD + 51 MathValueRecords
    // = 214 bytes); it has no count field, so the fixed layout is the declaration's
    // essence and no pseudo-count exists (OQ-2).
    internal const int ConstantsSize = 214, ConstantsValueRecordsAt = 8,
        ConstantsRecordCount = 51;
    internal const int KernInfoRecordSize = 8, GlyphPartSize = 10,
        GlyphVariantRecordSize = 4, ValueRecordSize = 4;
    // MathValueRecord is one FWORD value plus one Optional/ZeroAllowed deviceOffset.
    internal const int ValueValueAt = 0, ValueDeviceOffsetAt = 2;
    // The GlyphPart flag word: 0x0001 EXTENDER_FLAG is the only defined bit; 0xFFFE is
    // the reserved ridge (the OQ-5 observation, never a refusal here).
    internal const ushort PartExtenderFlag = 0x0001, PartReservedMask = 0xFFFE;
}

// The MATH root (T4-10 section 2): the packed 1.0-only version word over the three
// Offset16 table pointers. Every slot is declared Optional/ZeroAllowed (OQ-1's lean
// ruling: the spec's NULL-note lineage is an incomplete enumeration and the 19-host
// wire matrix carries real NULL instances on all three heads - Empty/Partial1-4 and
// PretendToBeCambria - so a Required reading would refuse shipping fonts); the
// semantic layer's combination contracts are where the hard faces live. The HeaderEnd
// floor makes all three edges strictly forward, which is the declaration-level DAG
// guarantee (the JSTF root shape): an offset pointing back at the version word
// refuses natively as StorageBeforeRecords.
[BinaryTable(MathLayout.Tag, MathLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { MathLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ConstantsOffset", WireType.UInt16, MathLayout.ConstantsOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GlyphInfoOffset", WireType.UInt16, MathLayout.GlyphInfoOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("VariantsOffset", WireType.UInt16, MathLayout.VariantsOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryDerived("HeaderEnd", Constant = MathLayout.Header10)]
[BinaryReference("Constants", "ConstantsOffset", typeof(MathConstantsView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("GlyphInfo", "GlyphInfoOffset", typeof(MathGlyphInfoView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("Variants", "VariantsOffset", typeof(MathVariantsView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
public readonly ref partial struct MathView { }
