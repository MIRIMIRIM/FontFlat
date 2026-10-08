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
public readonly ref partial struct KernSubtable0View
{
    // The typed format 0 body read over the exact declared-length slice. Hard faces:
    // the declared-length contract (OQ-1 - declared < computed PairsEnd refuses, the
    // OpenSans real negative witness; the length-driven sequence's authority), then
    // the per-pair numGlyphs domain and strict order (OQ-4 / the T0 rejection face).
    // The search triple is the OQ-2 observation face: recomputed under the frozen R6b
    // convention and recorded, never refused.
    internal ReadStatus ReadBody(int numGlyphs, bool materialize, out KernFormat0Body? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLength(out ushort declared, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPairCount(out ushort pairCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPairsEnd(out ulong pairsEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (declared < pairsEnd)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 2),
                out diagnostic, declared, pairsEnd);
        if (!budget.Work(declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var pairs = materialize ? new KernPair[pairCount] : null;
        long previousKey = -1;
        for (int i = 0; i < pairCount; i++)
        {
            status = TryGetKernPair(i, out var pair, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = KernSemantics.CheckPair(numGlyphs, ref previousKey, pair.Left, pair.Right,
                (ulong)(KernSubtable0Layout.PairsAt + i * KernSubtable0Layout.PairStride),
                Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (pairs is not null)
                pairs[i] = new(pair.Left, pair.Right, pair.Value);
        }
        status = TryGetSearchRange(out ushort searchRange, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetEntrySelector(out ushort entrySelector, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetRangeShift(out ushort rangeShift, out diagnostic);
        if (status != ReadStatus.Done) return status;
        KernSemantics.CanonicalSearchFields(pairCount, out int canonicalRange,
            out int canonicalSelector, out int canonicalShift);
        if (materialize)
        {
            if (!budget.Buffer(8L * pairCount + 16, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            model = new(pairs!, searchRange, entrySelector, rangeShift, canonicalRange,
                canonicalSelector, canonicalShift, Location, declared);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
