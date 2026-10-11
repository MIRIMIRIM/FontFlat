using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of HVAR 1.0. Reference bounds and the
/// version gate are declared; this partial adds the map-domain semantics (OQ-T5-01-3:
/// a non-NULL map must carry at least one entry) and the implicit-map domain check
/// (OQ-T5-01-8: with the advance map NULL, VarData[0]'s declared item count must
/// cover the glyph-count context, since glyph IDs become inner-level indices).
/// axisCount and numGlyphs are explicit inputs - the T4 GID-domain discipline; no
/// fvar cross-check exists until T5-02 (card 1).</summary>
public readonly ref partial struct HvarView
{
    public ReadStatus ValidateTable(int axisCount, int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(axisCount, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int axisCount, int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic, (ulong)axisCount, 0);
        status = TryOpenItemVariationStore(out var storeView, out bool storePresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!storePresent)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, HvarLayout.VariationStoreAt),
                out diagnostic, 0ul, (ulong)_data.Length);
        status = storeView.Validate(axisCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ValidateMap(mapSlot: HvarLayout.AdvanceMapAt, out bool advancePresent,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ValidateMap(mapSlot: HvarLayout.LsbMapAt, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ValidateMap(mapSlot: HvarLayout.RsbMapAt, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!advancePresent)
            return ValidateImplicitDomain(storeView, numGlyphs, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the complete table into an owned <see cref="HvarTable"/> with
    /// every delta row and map entry materialized (card 3.2). LONG_WORDS stores are
    /// refused here through the typed VarData read (OQ-T5-01-1).</summary>
    public ReadStatus TryReadTable(int axisCount, int numGlyphs, out HvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(axisCount, numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int axisCount, int numGlyphs, out HvarTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(axisCount, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryOpenItemVariationStore(out var storeView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = storeView.TryRead(axisCount, out var store, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryReadMap(mapSlot: HvarLayout.AdvanceMapAt, out var advanceMap,
            out uint advanceOffset, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryReadMap(mapSlot: HvarLayout.LsbMapAt, out var lsbMap, out uint lsbOffset,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryReadMap(mapSlot: HvarLayout.RsbMapAt, out var rsbMap, out uint rsbOffset,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int consumed = HvarLayout.HeaderLength;
        consumed = Math.Max(consumed, checked((int)ItemVariationStoreOffset + store!.ConsumedLength));
        consumed = IncludeMap(consumed, advanceOffset, advanceMap);
        consumed = IncludeMap(consumed, lsbOffset, lsbMap);
        consumed = IncludeMap(consumed, rsbOffset, rsbMap);
        model = new(store, advanceMap, lsbMap, rsbMap, numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Map-domain pass for one optional map: declared bounds through the native opener,
    // then the non-NULL-means-non-empty rule (OQ-T5-01-3) and the shared structural
    // pass of the map view (entryFormat domain, packed-entry extent).
    private ReadStatus ValidateMap(int mapSlot, out bool present, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        present = false;
        ReadStatus status;
        DeltaSetIndexMapView mapView;
        switch (mapSlot)
        {
            case HvarLayout.AdvanceMapAt:
                status = TryOpenAdvanceWidthMapping(out mapView, out present, out diagnostic);
                break;
            case HvarLayout.LsbMapAt:
                status = TryOpenLsbMapping(out mapView, out present, out diagnostic);
                break;
            default:
                status = TryOpenRsbMapping(out mapView, out present, out diagnostic);
                break;
        }
        if (status != ReadStatus.Done || !present) return status;
        // The format-0 re-anchor (OQ-T6-03-4): the shared map's version domain widened
        // to {0,1} for COLR, so the HVAR face checks the format explicitly - the
        // diagnostic is the old version-gate refusal, byte for byte.
        status = mapView.RequireFormatZero(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = mapView.Validate(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = mapView.TryGetMapCount(out ushort mapCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (mapCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                mapView.Location.AtLocal(DataField.Count, DeltaSetIndexMapLayout.MapCountAt),
                out diagnostic, 0, 1);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryReadMap(int mapSlot, out DeltaSetIndexMapTable? map, out uint offset,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        map = null;
        ReadStatus status;
        DeltaSetIndexMapView mapView;
        switch (mapSlot)
        {
            case HvarLayout.AdvanceMapAt:
                status = TryGetAdvanceWidthMappingOffset(out offset, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (offset == 0) return MapDone(out diagnostic);
                status = TryOpenAdvanceWidthMapping(out mapView, out _, out diagnostic);
                break;
            case HvarLayout.LsbMapAt:
                status = TryGetLsbMappingOffset(out offset, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (offset == 0) return MapDone(out diagnostic);
                status = TryOpenLsbMapping(out mapView, out _, out diagnostic);
                break;
            default:
                status = TryGetRsbMappingOffset(out offset, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (offset == 0) return MapDone(out diagnostic);
                status = TryOpenRsbMapping(out mapView, out _, out diagnostic);
                break;
        }
        if (status != ReadStatus.Done) return status;
        status = mapView.RequireFormatZero(out diagnostic);
        if (status != ReadStatus.Done) return status;
        return mapView.TryRead(out map, out diagnostic, budget);
    }

    private static ReadStatus MapDone(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }

    // OQ-T5-01-8: with the advance map NULL the glyph ID is the inner-level index of
    // outer zero, so the implicit domain is VarData[0].itemCount; a declared domain
    // smaller than the glyph-count context is pinned here at validation (the store
    // dereference keeps its own bounds check either way).
    private ReadStatus ValidateImplicitDomain(ItemVariationStoreView storeView, int numGlyphs,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = storeView.TryGetItemVariationDataCount(out ushort dataCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (dataCount == 0) return MapDone(out diagnostic);
        status = storeView.TryOpenItemVariationData(0, out var varDataView,
            out bool present, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!present) return MapDone(out diagnostic);
        status = varDataView.TryGetItemCount(out ushort itemCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (itemCount < numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                varDataView.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)numGlyphs, itemCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int IncludeMap(int consumed, uint offset, DeltaSetIndexMapTable? map) =>
        map is null ? consumed : Math.Max(consumed, checked((int)offset + map.ConsumedLength));
}
