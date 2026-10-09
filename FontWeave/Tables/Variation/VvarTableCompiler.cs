using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The VVAR root writer (HvarTableCompiler twin): one planned, atomically
/// published whole-table emission. The canonical layout is fixed - header(24), then
/// the ItemVariationStore (its internal canonical order), then the advance/tsb/bsb/
/// vOrg maps - and Offset32 fixups carry no 64K pressure, so exactly one layout
/// candidate exists and there is no second-chance refusal path (card 4, the
/// registered T5-01 difference). All fixups, counts and budget checks complete
/// before any byte is written; a refused plan never touches the destination. A
/// source whose maps share one subtable (the NotoSansJP/CJK witness shape) re-emits
/// it once per slot under the canonical order - wire-equivalent by registration
/// (OQ-T5-04-8), never byte-identical to the shared form.</summary>
public static class VvarTableCompiler
{
    /// <summary>Explicit root-model factory: the store and every map are
    /// caller-supplied and never inferred; axisCount must equal the store's declared
    /// count (the explicit T5-02-forward contract) and the glyph-count context drives
    /// the same implicit-map domain the reader enforces (OQ-T5-04-5).</summary>
    public static ReadStatus TryCreate(VariationStoreTable store, DeltaSetIndexMapTable? advanceMap,
        DeltaSetIndexMapTable? tsbMap, DeltaSetIndexMapTable? bsbMap, DeltaSetIndexMapTable? vOrgMap,
        int axisCount, int numGlyphs, out VvarTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
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
            !SameSource(location, tsbMap) || !SameSource(location, bsbMap) ||
            !SameSource(location, vOrgMap))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        if (advanceMap is null && store.DataCount > 0 && store.Data[0] is { } first &&
            first.ItemCount < numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                first.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)numGlyphs, first.ItemCount);
        if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(store, advanceMap, tsbMap, bsbMap, vOrgMap, numGlyphs, location, 0);
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

    public static ReadStatus TryCompile(VvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(VvarTable model, out byte[]? output,
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

    public static ReadStatus TryWrite(VvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
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
    internal static int Measure(VvarTable model)
    {
        int length = VvarLayout.HeaderLength + VariationStoreTableCompiler.Measure(model.Store);
        if (model.AdvanceHeightMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.AdvanceHeightMapping);
        if (model.TsbMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.TsbMapping);
        if (model.BsbMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.BsbMapping);
        if (model.VOrgMapping is not null)
            length += DeltaSetIndexMapTableCompiler.Measure(model.VOrgMapping);
        return length;
    }

    // The single canonical candidate: every child size, every fixup value and the
    // whole budget admission happen here, before any byte exists.
    private static ReadStatus TryPlan(VvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        int measured = Measure(model);
        long emission = measured + model.Store.DataCount;
        if (model.AdvanceHeightMapping is not null) emission += model.AdvanceHeightMapping.MapCount;
        if (model.TsbMapping is not null) emission += model.TsbMapping.MapCount;
        if (model.BsbMapping is not null) emission += model.BsbMapping.MapCount;
        if (model.VOrgMapping is not null) emission += model.VOrgMapping.MapCount;
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(measured, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        length = measured;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path.
    private static void WritePlanned(VvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(VvarView.TryWriteVersion(destination, 0x00010000,
            out _, location));
        int storeLength = VariationStoreTableCompiler.Measure(model.Store);
        int advanceAt = VvarLayout.HeaderLength + storeLength;
        int tsbAt = advanceAt + (model.AdvanceHeightMapping is null ? 0 :
            DeltaSetIndexMapTableCompiler.Measure(model.AdvanceHeightMapping));
        int bsbAt = tsbAt + (model.TsbMapping is null ? 0 :
            DeltaSetIndexMapTableCompiler.Measure(model.TsbMapping));
        int vOrgAt = bsbAt + (model.BsbMapping is null ? 0 :
            DeltaSetIndexMapTableCompiler.Measure(model.BsbMapping));
        WriteStoreSlot(destination, location);
        WriteMapSlot(destination, location, VvarLayout.AdvHeightMapAt,
            model.AdvanceHeightMapping is not null, advanceAt);
        WriteMapSlot(destination, location, VvarLayout.TsbMapAt, model.TsbMapping is not null, tsbAt);
        WriteMapSlot(destination, location, VvarLayout.BsbMapAt, model.BsbMapping is not null, bsbAt);
        WriteMapSlot(destination, location, VvarLayout.VOrgMapAt, model.VOrgMapping is not null, vOrgAt);
        VariationStoreTableCompiler.WritePlanned(model.Store,
            destination.Slice(VvarLayout.HeaderLength));
        if (model.AdvanceHeightMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.AdvanceHeightMapping,
                destination.Slice(advanceAt));
        if (model.TsbMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.TsbMapping,
                destination.Slice(tsbAt));
        if (model.BsbMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.BsbMapping,
                destination.Slice(bsbAt));
        if (model.VOrgMapping is not null)
            DeltaSetIndexMapTableCompiler.WritePlanned(model.VOrgMapping,
                destination.Slice(vOrgAt));
    }

    // The store slot is required (its target always exists in the canonical layout);
    // a refusal on a preflighted plan is an invariant breach (GDEF root precedent).
    private static void WriteStoreSlot(Span<byte> destination, DataLocation location)
    {
        ReadStatus status = VvarView.TryGetItemVariationStoreOffsetValue(true, 0UL,
            VvarLayout.HeaderLength, out uint encoded, out _, location);
        if (status == ReadStatus.Done)
            status = VvarView.TryWriteItemVariationStoreOffset(destination, encoded, out _, location);
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
            case VvarLayout.AdvHeightMapAt:
                status = VvarView.TryGetAdvanceHeightMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint advanceEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = VvarView.TryWriteAdvanceHeightMappingOffset(destination, advanceEncoded,
                        out _, location);
                break;
            case VvarLayout.TsbMapAt:
                status = VvarView.TryGetTsbMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint tsbEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = VvarView.TryWriteTsbMappingOffset(destination, tsbEncoded,
                        out _, location);
                break;
            case VvarLayout.BsbMapAt:
                status = VvarView.TryGetBsbMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint bsbEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = VvarView.TryWriteBsbMappingOffset(destination, bsbEncoded,
                        out _, location);
                break;
            default:
                status = VvarView.TryGetVOrgMappingOffsetValue(present, 0UL, (ulong)target,
                    out uint vOrgEncoded, out _, location);
                if (status == ReadStatus.Done)
                    status = VvarView.TryWriteVOrgMappingOffset(destination, vOrgEncoded,
                        out _, location);
                break;
        }
        CoverageTableCompiler.RequireWritten(status);
    }
}
