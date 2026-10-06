using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of one ItemVariationData subtable: the structural pass
/// (row-width formula, byte extent, word/short relation) is the card's hand-written
/// boundary. LONG_WORDS is tolerated structurally (the extent is computable, raw
/// observation is legal, OQ-T5-01-1) while the typed materialization refuses it -
/// the only T5-01 typed consumer is HVAR, whose read therefore rejects the
/// COLR-oriented 32-bit rows at WordDeltaCount with the raw value in Detail.</summary>
public readonly ref partial struct ItemVariationDataView
{
    /// <summary>Packed delta-row width in bytes: regionIndexCount plus the masked word
    /// delta count, doubled under LONG_WORDS (spec otvarcommonformats.md DeltaSet:
    /// long deltas first - int32 under the flag / int16 without - then short deltas -
    /// int16 under the flag / int8 without).</summary>
    internal static long DeltaRowBytes(int regionIndexCount, ushort wordDeltaCount)
    {
        long shorts = (long)regionIndexCount + (wordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask);
        return (wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0 ? 2L * shorts : shorts;
    }

    /// <summary>The strict structural pass: the masked wordDeltaCount must not exceed
    /// regionIndexCount (spec: "must be less than or equal to regionIndexCount"), the
    /// declared regionIndexes extent must be present, and every delta row must fit the
    /// window. Materializes nothing.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(out _, out _, out diagnostic, budget);

    /// <summary>Structural facts shared by validation and materialization: the declared
    /// subtable length and the packed row width. Pure inspection, no owned output.</summary>
    internal ReadStatus Inspect(out int consumed, out long rowBytes, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        consumed = 0;
        rowBytes = 0;
        ReadStatus status = TryGetRegionIndexCount(out ushort regionIndexCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetWordDeltaCount(out ushort wordDeltaCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemCount(out ushort itemCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((ulong)(wordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask) > regionIndexCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, ItemVariationDataLayout.WordDeltaCountAt),
                out diagnostic,
                (ulong)(wordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask), regionIndexCount);
        status = TryGetRegionIndexesExtent(out _, out _, out ulong indexesEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        rowBytes = DeltaRowBytes(regionIndexCount, wordDeltaCount);
        long rowsBytes = checked(itemCount * rowBytes);
        long declared = checked((long)indexesEnd + rowsBytes);
        if (!budget.Work(declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (declared > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, indexesEnd), out diagnostic,
                (ulong)_data.Length, (ulong)declared);
        consumed = (int)declared;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Typed materialization of every delta row. LONG_WORDS rows are refused
    /// here (OQ-T5-01-1): the T5-01 product consumers are HVAR-domain and the spec
    /// limits long words to COLR (T6-03); structural observation stays legal above.</summary>
    public ReadStatus TryRead(out ItemVariationDataTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out ItemVariationDataTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetWordDeltaCount(out ushort wordDeltaCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Flags, ItemVariationDataLayout.WordDeltaCountAt),
                out diagnostic, wordDeltaCount, 0);
        status = Inspect(out int consumed, out long rowBytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetRegionIndexCount(out ushort regionIndexCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemCount(out ushort itemCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * regionIndexCount + 4L * itemCount * regionIndexCount, Location, out diagnostic) ||
            !budget.Work((long)itemCount * regionIndexCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var regionIndexes = new ushort[regionIndexCount];
        for (int i = 0; i < regionIndexCount; i++)
        {
            status = TryGetRegionIndexes(i, out var indexRecord, out diagnostic);
            if (status != ReadStatus.Done) return status;
            regionIndexes[i] = indexRecord.RegionIndex;
        }
        // Long deltas first (int32 under LONG_WORDS, else int16), then short deltas
        // (int16 under LONG_WORDS, else int8) - exactly the widths the row formula
        // priced, so round-trips re-emit the source width class unchanged (card 4).
        int words = wordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask;
        bool longWords = (wordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0;
        var rows = new int[itemCount][];
        int rowsAt = ItemVariationDataLayout.RegionIndexesAt + 2 * regionIndexCount;
        for (int row = 0; row < itemCount; row++)
        {
            var deltas = new int[regionIndexCount];
            int at = rowsAt;
            for (int i = 0; i < regionIndexCount; i++)
            {
                if (i < words)
                {
                    deltas[i] = longWords ? ReadInt32(at) : ReadInt16(at);
                    at += longWords ? 4 : 2;
                }
                else
                {
                    deltas[i] = longWords ? ReadInt16(at) : RawBytes[at] << 24 >> 24;
                    at += longWords ? 2 : 1;
                }
            }
            rows[row] = deltas;
            rowsAt += (int)rowBytes;
        }
        model = new(itemCount, wordDeltaCount, regionIndexes, rows, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private int ReadInt16(int at) =>
        global::System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(RawBytes.Slice(at));

    private int ReadInt32(int at) =>
        (int)global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(RawBytes.Slice(at));
}
