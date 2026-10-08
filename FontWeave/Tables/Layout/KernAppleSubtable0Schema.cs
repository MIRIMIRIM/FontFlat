using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernAppleSubtable0Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV1, PairCountAt = 8,
        SearchRangeAt = 10, EntrySelectorAt = 12, RangeShiftAt = 14,
        PairsAt = KernLayout.SubtableBodyV1, PairStride = KernLayout.PairStride;
}

// The Apple v1.0 format 0 subtable (read-side typed depth, OQ-6). No version field -
// the dialect is fixed by the root's version word (section 7), so this is a
// version-less view (the AttachList family shape). The header differs from v0 (u32
// length, the coverage word with reversed flag/format halves, tupleIndex), which makes
// it a legal separate view under the one-layout rule (the BaseTagList precedent);
// the body declarations mirror the v0 pair shape word for word. The same OQ-2 search
// observation face and OQ-1 declared-length contract apply through the shared body
// shape, and the walk slices exactly the declared u32 length.
[BinaryTable(KernLayout.Tag, KernAppleSubtable0Layout.PairsAt, ContractVersion = 1)]
[BinaryScalar("Length", WireType.UInt32, 0, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("TupleIndex", WireType.UInt16, 6, DataField.Payload)]
[BinaryScalar("PairCount", WireType.UInt16, KernAppleSubtable0Layout.PairCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("SearchRange", WireType.UInt16, KernAppleSubtable0Layout.SearchRangeAt, DataField.Payload)]
[BinaryScalar("EntrySelector", WireType.UInt16, KernAppleSubtable0Layout.EntrySelectorAt, DataField.Payload)]
[BinaryScalar("RangeShift", WireType.UInt16, KernAppleSubtable0Layout.RangeShiftAt, DataField.Payload)]
[BinaryDerived("PairsEnd", Constant = KernAppleSubtable0Layout.PairsAt, Count = "PairCount",
    Stride = KernAppleSubtable0Layout.PairStride)]
[BinaryArray("KernPair", "PairCount", KernAppleSubtable0Layout.PairsAt,
    KernAppleSubtable0Layout.PairStride)]
[BinaryElement("KernPair", "Left", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("KernPair", "Right", WireType.UInt16, 2, DataField.GlyphId)]
[BinaryElement("KernPair", "Value", WireType.Int16, 4, DataField.Payload)]
public readonly ref partial struct KernAppleSubtable0View { }
