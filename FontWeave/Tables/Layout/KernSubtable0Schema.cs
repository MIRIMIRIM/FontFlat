using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernSubtable0Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV0, PairCountAt = 6,
        SearchRangeAt = 8, EntrySelectorAt = 10, RangeShiftAt = 12,
        PairsAt = KernLayout.SubtableBodyV0, PairStride = KernLayout.PairStride;
}

// One OT v0 format 0 subtable, opened at its own start (the walk slices exactly the
// declared length): the version gate (a non-zero subtable version is the native
// refusal, T0 rejection face), the declared length the walk steps by, the coverage
// word, the search triple and the pair cells. The search fields are the OQ-2
// observation face - plain payload words, never a refusal; the semantic partial owns
// the canonical recomputation and the pair order/GID domain (OQ-4).
[BinaryTable(KernLayout.Tag, KernSubtable0Layout.PairsAt, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { KernLayout.Version0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("PairCount", WireType.UInt16, KernSubtable0Layout.PairCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("SearchRange", WireType.UInt16, KernSubtable0Layout.SearchRangeAt, DataField.Payload)]
[BinaryScalar("EntrySelector", WireType.UInt16, KernSubtable0Layout.EntrySelectorAt, DataField.Payload)]
[BinaryScalar("RangeShift", WireType.UInt16, KernSubtable0Layout.RangeShiftAt, DataField.Payload)]
// The declared pair storage floor 14 + 6*pairCount: the OQ-1 length contract compares
// the declared subtable length against it (declared < computed refuses, the OpenSans
// real negative witness).
[BinaryDerived("PairsEnd", Constant = KernSubtable0Layout.PairsAt, Count = "PairCount",
    Stride = KernSubtable0Layout.PairStride)]
[BinaryArray("KernPair", "PairCount", KernSubtable0Layout.PairsAt, KernSubtable0Layout.PairStride)]
[BinaryElement("KernPair", "Left", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("KernPair", "Right", WireType.UInt16, 2, DataField.GlyphId)]
[BinaryElement("KernPair", "Value", WireType.Int16, 4, DataField.Payload)]
public readonly ref partial struct KernSubtable0View { }
