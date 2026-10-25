using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>Compile-side model admission and emission for one ItemVariationData
/// subtable. Width classes are taken from the packed wordDeltaCount and never
/// rewritten (card 4: no cross-width rewriting, no widening - the OQ-T5-01-5 finite
/// refusal fires when an edited value leaves its slot's domain). LONG_WORDS is a
/// typed-read refusal domain (OQ-T5-01-1), so parts factories refuse it too: the
/// T5-01 emission face is HVAR-domain, COLR-oriented long words stay T6-03.</summary>
public static class ItemVariationDataTableCompiler
{
    public static ReadStatus TryCreate(ushort wordDeltaCount, IReadOnlyList<ushort> regionIndexes,
        IReadOnlyList<int[]> rows, out ItemVariationDataTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(wordDeltaCount, regionIndexes, rows, out model, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ushort wordDeltaCount, IReadOnlyList<ushort> regionIndexes,
        IReadOnlyList<int[]> rows, out ItemVariationDataTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(regionIndexes);
        ArgumentNullException.ThrowIfNull(rows);
        int regionIndexCount = regionIndexes.Count;
        if ((wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Flags, ItemVariationDataLayout.WordDeltaCountAt),
                out diagnostic, wordDeltaCount, 0);
        int words = wordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask;
        if (words > regionIndexCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, ItemVariationDataLayout.WordDeltaCountAt),
                out diagnostic, (ulong)words, (ulong)regionIndexCount);
        int itemCount = rows.Count;
        // Both counts are UInt16 scalars on the wire; the plan refuses anything past
        // the word instead of letting the emission narrow it.
        if (itemCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)itemCount, (ulong)ushort.MaxValue);
        if (regionIndexCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ItemVariationDataLayout.RegionIndexCountAt),
                out diagnostic, (ulong)regionIndexCount, (ulong)ushort.MaxValue);
        for (int row = 0; row < itemCount; row++)
        {
            int[] deltas = rows[row];
            if (deltas.Length != regionIndexCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                    out diagnostic, (ulong)deltas.Length, (ulong)regionIndexCount);
            for (int i = 0; i < regionIndexCount; i++)
            {
                // The width class of slot i is fixed by the packed field; an edited
                // value that leaves it is the finite refusal (OQ-T5-01-5), never a
                // silent widening. Word slots are int32 under LONG_WORDS and int16
                // without; short slots are int16 under the flag and int8 without.
                long minimum, maximum;
                if (i < words)
                    (minimum, maximum) = (wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0
                        ? (int32Min, int32Max) : (int16Min, int16Max);
                else
                    (minimum, maximum) = (wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0
                        ? (int16Min, int16Max) : (int8Min, int8Max);
                if (deltas[i] < minimum || deltas[i] > maximum)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Payload,
                            (ulong)(ItemVariationDataLayout.RegionIndexesAt + 2L * regionIndexCount +
                            (long)row * ItemVariationDataView.DeltaRowBytes(regionIndexCount, wordDeltaCount)),
                            row), out diagnostic,
                        unchecked((ulong)(long)deltas[i]), (ulong)maximum);
            }
        }
        if (!budget.Work((long)itemCount * regionIndexCount, location, out diagnostic) ||
            !budget.Buffer(4L * itemCount * regionIndexCount, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new((ushort)itemCount, wordDeltaCount, regionIndexes.ToArray(),
            rows.Select(static row => (int[])row.Clone()).ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private const long int32Min = int.MinValue, int32Max = int.MaxValue,
        int16Min = short.MinValue, int16Max = short.MaxValue,
        int8Min = sbyte.MinValue, int8Max = sbyte.MaxValue;

    /// <summary>Wire length of the canonical emission (header, regionIndexes, rows in
    /// the source width classes); budget-free arithmetic for the store planner.</summary>
    internal static int Measure(ItemVariationDataTable model)
    {
        return ItemVariationDataLayout.RegionIndexesAt
            + 2 * model.RegionIndexCount
            + checked((int)(model.ItemCount * ItemVariationDataView.DeltaRowBytes(
                model.RegionIndexCount, model.WordDeltaCount)));
    }

    /// <summary>Root-writer emission: plans without a budget (the owning compiler
    /// preflighted the destination) and writes one body at the slice origin.</summary>
    internal static void WritePlanned(ItemVariationDataTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(ItemVariationDataView.TryWriteItemCount(destination,
            model.ItemCount, out _, location));
        CoverageTableCompiler.RequireWritten(ItemVariationDataView.TryWriteWordDeltaCount(destination,
            model.WordDeltaCount, out _, location));
        CoverageTableCompiler.RequireWritten(ItemVariationDataView.TryWriteRegionIndexCount(destination,
            (ushort)model.RegionIndexCount, out _, location));
        for (int i = 0; i < model.RegionIndexCount; i++)
            CoverageTableCompiler.RequireWritten(ItemVariationDataView.TryWriteRegionIndexes(destination,
                (ulong)model.RegionIndexCount, new ItemVariationDataView.RegionIndexesData(i,
                    model.RegionIndexes[i]), out _, location));
        int words = model.WordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask;
        bool longWords = model.LongWords;
        int at = ItemVariationDataLayout.RegionIndexesAt + 2 * model.RegionIndexCount;
        for (int row = 0; row < model.ItemCount; row++)
        {
            var deltas = model.Rows[row];
            for (int i = 0; i < model.RegionIndexCount; i++)
            {
                if (i < words)
                {
                    if (longWords)
                    {
                        global::System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
                            destination.Slice(at), deltas[i]);
                        at += 4;
                    }
                    else
                    {
                        global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                            destination.Slice(at), (short)deltas[i]);
                        at += 2;
                    }
                }
                else if (longWords)
                {
                    global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                        destination.Slice(at), (short)deltas[i]);
                    at += 2;
                }
                else
                {
                    // Signed byte slot: the value already fits sbyte by admission;
                    // the two's-complement truncation is the wire encoding.
                    destination[at] = (byte)(deltas[i] & 0xFF);
                    at += 1;
                }
            }
        }
    }
}
