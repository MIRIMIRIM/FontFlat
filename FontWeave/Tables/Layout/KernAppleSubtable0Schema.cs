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
public readonly ref partial struct KernAppleSubtable0View
{
    // The Apple format 0 body read: the v0 face minus the version field (the dialect
    // is fixed by the root, section 7), with the u32 declared length driving the same
    // OQ-1 contract and the same OQ-2 search observation. The pair checks are the
    // shared KernSemantics rules - the dialect head split never forks a rule.
    internal ReadStatus ReadBody(int numGlyphs, bool materialize, out KernFormat0Body? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLength(out uint declared, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPairCount(out ushort pairCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPairsEnd(out ulong pairsEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (declared < pairsEnd)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0),
                out diagnostic, declared, pairsEnd);
        if (!budget.Work((long)declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var pairs = materialize ? new KernPair[pairCount] : null;
        long previousKey = -1;
        for (int i = 0; i < pairCount; i++)
        {
            status = TryGetKernPair(i, out var pair, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = KernSemantics.CheckPair(numGlyphs, ref previousKey, pair.Left, pair.Right,
                (ulong)(KernAppleSubtable0Layout.PairsAt + i * KernAppleSubtable0Layout.PairStride),
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
                canonicalSelector, canonicalShift, Location, checked((int)declared));
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
