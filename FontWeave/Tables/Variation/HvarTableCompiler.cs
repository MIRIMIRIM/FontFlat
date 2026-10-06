using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The first HVAR root writer (GdefTableCompiler shape): one planned,
/// atomically published whole-table emission. The canonical layout is fixed -
/// header(20), then the ItemVariationStore (its internal canonical order), then the
/// advance/lsb/rsb maps - and Offset32 fixups carry no 64K pressure, so unlike the
/// T4-02/03/05 roots there is exactly one layout candidate and no second-chance
/// refusal path (card 4, registered difference). All fixups, counts and budget
/// checks complete before any byte is written; a refused plan never touches the
/// destination. Unedited round-trips of the three primary witnesses are
/// byte-identical because the canonical layout is the layout they already carry;
/// maps-before-store sources re-emit wire-equivalent under the canonical order.</summary>
public static class HvarTableCompiler
{
    /// <summary>Explicit root-model factory: the store and every map are
    /// caller-supplied and never inferred; axisCount must equal the store's declared
    /// count (the explicit T5-02-forward contract) and the glyph-count context drives
    /// the same implicit-map domain the reader enforces (OQ-T5-01-8).</summary>
    public static ReadStatus TryCreate(VariationStoreTable store, DeltaSetIndexMapTable? advanceMap,
        DeltaSetIndexMapTable? lsbMap, DeltaSetIndexMapTable? rsbMap, int axisCount, int numGlyphs,
        out HvarTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(store);
        var budget = new BuildBudget(limits);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (store.AxisCount != axisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, VariationRegionLayout.AxisCountAt),
                out diagnostic, (ulong)store.AxisCount, (ulong)axisCount);
        if (!SameSource(location, store) || !SameSource(location, advanceMap) ||
            !SameSource(location, lsbMap) || !SameSource(location, rsbMap))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        if (advanceMap is null && store.DataCount > 0 && store.Data[0] is { } first &&
            first.ItemCount < numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                first.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)numGlyphs, first.ItemCount);
        if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(store, advanceMap, lsbMap, rsbMap, numGlyphs, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, object? model)
    {
        DataLocation child = model switch
        {
            VariationStoreTable inner => inner.Location,
            DeltaSetIndexMapTable map => map.Location,
            _ => location,
        };
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    public static ReadStatus TryCompile(HvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(HvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(HvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission, including the emission work, completes before the
        // destination capacity check (spec 7.3; the destination is untouched on refusal).
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Wire length of the canonical emission; budget-free arithmetic for
    /// callers that only measure.</summary>
    internal static int Measure(HvarTable model)
    {
        int length = HvarLayout.HeaderLength + VariationStoreTableCompiler.Measure(model.Store);
        if (model.AdvanceWidthMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.AdvanceWidthMapping);
        if (model.LsbMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.LsbMapping);
        if (model.RsbMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.RsbMapping);
        return length;
    }

    // The single canonical candidate: every child size, every fixup value and the
    // whole budget admission happen here, before any byte exists.
    private static ReadStatus TryPlan(HvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        int measured = Measure(model);
        long emission = measured + model.Store.DataCount;
        if (model.AdvanceWidthMapping is not null) emission += model.AdvanceWidthMapping.MapCount;
        if (model.LsbMapping is not null) emission += model.LsbMapping.MapCount;
        if (model.RsbMapping is not null) emission += model.RsbMapping.MapCount;
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(measured, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        length = measured;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path.
    private static void WritePlanned(HvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(HvarView.TryWriteVersion(destination, 0x00010000,
            out _, location));
        int storeLength = VariationStoreTableCompiler.Measure(model.Store);
        int advanceAt = HvarLayout.HeaderLength + storeLength;
        int lsbAt = advanceAt + (model.AdvanceWidthMapping is null ? 0 :
            DeltaSetIndexMapTableCompiler.Measure(model.AdvanceWidthMapping));
        int rsbAt = lsbAt + (model.LsbMapping is null ? 0 :
            DeltaSetIndexMapTableCompiler.Measure(model.LsbMapping));
        WriteStoreSlot(destination, location);
        WriteMapSlot(destination, location, HvarLayout.AdvanceMapAt,
            model.AdvanceWidthMapping is not null, advanceAt);
        WriteMapSlot(destination, location, HvarLayout.LsbMapAt, model.LsbMapping is not null, lsbAt);
        WriteMapSlot(destination, location, HvarLayout.RsbMapAt, model.RsbMapping is not null, rsbAt);
        VariationStoreTableCompiler.WritePlanned(model.Store,
            destination.Slice(HvarLayout.HeaderLength));
        if (model.AdvanceWidthMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.AdvanceWidthMapping,
                destination.Slice(advanceAt));
        if (model.LsbMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.LsbMapping, destination.Slice(lsbAt));
        if (model.RsbMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.RsbMapping, destination.Slice(rsbAt));
    }

    // The store slot is required (its target always exists in the canonical layout);
    // a refusal on a preflighted plan is an invariant breach (GDEF root precedent).
    private static void WriteStoreSlot(Span<byte> destination, DataLocation location)
    {
        ReadStatus status = HvarView.TryGetItemVariationStoreOffsetValue(true, 0UL,
            HvarLayout.HeaderLength, out uint encoded, out _, location);
        if (status == ReadStatus.Done)
            status = HvarView.TryWriteItemVariationStoreOffset(destination, encoded, out _, location);
        CoverageTableCompiler.RequireWritten(status);
    }

    // Optional map slots: absent children encode as 0 through the same helpers
    // (present=false is not an error).
    private static void WriteMapSlot(Span<byte> destination, DataLocation location, int slotAt,
        bool present, int target)
    {
        ReadStatus status;
        switch (slotAt)
        {
            case HvarLayout.AdvanceMapAt:
                status = HvarView.TryGetAdvanceWidthMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint advanceEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = HvarView.TryWriteAdvanceWidthMappingOffset(destination, advanceEncoded,
                        out _, location);
                break;
            case HvarLayout.LsbMapAt:
                status = HvarView.TryGetLsbMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint lsbEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = HvarView.TryWriteLsbMappingOffset(destination, lsbEncoded, out _, location);
                break;
            default:
                status = HvarView.TryGetRsbMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint rsbEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = HvarView.TryWriteRsbMappingOffset(destination, rsbEncoded, out _, location);
                break;
        }
        CoverageTableCompiler.RequireWritten(status);
    }
}
