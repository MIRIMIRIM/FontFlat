using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of the ItemVariationStore: the version gate and every
/// reference opener are declared; this partial walks the region list and the counted
/// dataOffsets array, checking the cross-subtable relations the single views cannot
/// see - each VarData's regionIndexes against the declared region count (InvalidIndex
/// relation, card 3.1). LONG_WORDS stays a structural observation at this level
/// (OQ-T5-01-1); the typed materialization below refuses it through the VarData read.</summary>
public readonly ref partial struct ItemVariationStoreView
{
    /// <summary>The strict structural pass over the whole store. axisCount is the
    /// caller's explicit expectation (the T5-02-forward axis-count contract, card 1).</summary>
    public ReadStatus Validate(int axisCount, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(axisCount, out diagnostic, budget);
    }

    internal ReadStatus Validate(int axisCount, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Validate(axisCount, out _, out diagnostic, budget);

    internal ReadStatus Validate(int axisCount, out int regionCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        regionCount = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenVariationRegionList(out var regionListView, out bool regionListPresent,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!regionListPresent)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, ItemVariationStoreLayout.RegionListAt),
                out diagnostic, 0ul, (ulong)_data.Length);
        status = regionListView.Validate(axisCount, out regionCount, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemVariationDataCount(out ushort dataCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int outer = 0; outer < dataCount; outer++)
        {
            status = TryOpenItemVariationData(outer, out var varDataView,
                out bool varDataPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!varDataPresent) continue; // NULL data offset: no variation data (spec-legal).
            status = varDataView.Validate(out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = ValidateRegionIndexes(varDataView, regionCount, outer, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Typed materialization of the whole store: regions and every non-NULL
    /// VarData subtable fully decoded (LONG_WORDS refused by the VarData typed read
    /// unless the COLR consumption passes allowLongWords - OQ-T5-01-1/OQ-T6-03-5).
    /// All rows are materialized - the witness scale is bounded and the row-keyed API
    /// keeps a lazy path open for larger stores (card 3.2).</summary>
    public ReadStatus TryRead(int axisCount, out VariationStoreTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, bool allowLongWords = false)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(axisCount, out model, out diagnostic, budget, allowLongWords);
    }

    internal ReadStatus TryRead(int axisCount, out VariationStoreTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, bool allowLongWords = false)
    {
        model = null;
        ReadStatus status = Validate(axisCount, out int regionCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryOpenVariationRegionList(out var regionListView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = regionListView.TryRead(axisCount, out VariationRegionAxis[][]? regions,
            out _, out int regionsLength, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemVariationDataCount(out ushort dataCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetVariationRegionListOffset(out uint regionListOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var data = new ItemVariationDataTable?[dataCount];
        int consumed = Math.Max((int)regionListOffset + regionsLength, DataOffsetsEndOf(dataCount));
        for (int outer = 0; outer < dataCount; outer++)
        {
            status = TryOpenItemVariationData(outer, out var varDataView,
                out bool varDataPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!varDataPresent) continue;
            status = varDataView.TryRead(out var varData, out diagnostic, budget, allowLongWords);
            if (status != ReadStatus.Done) return status;
            data[outer] = varData;
            status = TryGetDataOffsets(outer, out var offsetRecord, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int end = (int)offsetRecord.ItemVariationDataOffset + varData!.ConsumedLength;
            if (end > consumed) consumed = end;
        }
        model = new(axisCount, regions!, data, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private int DataOffsetsEndOf(int dataCount) =>
        ItemVariationStoreLayout.DataOffsetsAt + ItemVariationStoreLayout.DataOffsetStride * dataCount;

    // The store-level relation: every region index must name a declared region
    // (card 3.1 "regionIndexes >= regionCount -> InvalidIndex").
    private ReadStatus ValidateRegionIndexes(ItemVariationDataView varDataView, int regionCount,
        int outer, out FontDiagnostic diagnostic)
    {
        ReadStatus status = varDataView.TryGetRegionIndexCount(out ushort indexCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < indexCount; i++)
        {
            status = varDataView.TryGetRegionIndexes(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.RegionIndex >= regionCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    varDataView.Location.AtLocal(DataField.Payload,
                        ItemVariationDataLayout.RegionIndexesAt + (ulong)i * ItemVariationDataLayout.RegionIndexStride, i),
                    out diagnostic, record.RegionIndex, (ulong)regionCount);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
