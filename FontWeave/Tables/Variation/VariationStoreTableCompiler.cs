using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>Compile-side model admission and emission for the whole ItemVariationStore
/// (format 1). The canonical layout is the declared read-side floor made real:
/// header, dataOffsets array, region list, then the VarData subtables in outer order -
/// the layout all three primary witnesses already carry, which is what makes the
/// unedited round-trip byte-identical (card 4). Offset32 targets carry no 64K
/// pressure, so there is no second layout candidate (the explicit T4-02/03/05
/// difference). Region axis coordinates re-emit as the raw F2Dot14 values they were
/// read as; edited triples must satisfy the same domain Validate enforces.</summary>
public static class VariationStoreTableCompiler
{
    public static ReadStatus TryCreate(int axisCount, IReadOnlyList<VariationRegionAxis[]> regions,
        IReadOnlyList<ItemVariationDataTable?> data, out VariationStoreTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(axisCount, regions, data, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(int axisCount, IReadOnlyList<VariationRegionAxis[]> regions,
        IReadOnlyList<ItemVariationDataTable?> data, out VariationStoreTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(data);
        if (axisCount is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ItemVariationStoreLayout.RegionListAt),
                out diagnostic, (ulong)axisCount, ushort.MaxValue);
        int regionCount = regions.Count;
        if (regionCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ItemVariationStoreLayout.DataCountAt),
                out diagnostic, (ulong)regionCount, ushort.MaxValue);
        if (data.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ItemVariationStoreLayout.DataCountAt),
                out diagnostic, (ulong)data.Count, ushort.MaxValue);
        if (!budget.Work((long)regionCount * axisCount + data.Count, location, out diagnostic) ||
            !budget.Buffer(6L * regionCount * axisCount + 4L * data.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var region in regions)
        {
            if (region.Length != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, VariationRegionLayout.AxisCountAt),
                    out diagnostic, (ulong)region.Length, (ulong)axisCount);
            for (int axis = 0; axis < axisCount; axis++)
            {
                var triple = region[axis];
                ReadStatus status = VariationRegionListView.ValidateAxisTriple(triple.StartCoord,
                    triple.PeakCoord, triple.EndCoord, location, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        foreach (var subtable in data)
        {
            if (subtable is null) continue;
            for (int i = 0; i < subtable.RegionIndexCount; i++)
                if (subtable.RegionIndexes[i] >= regionCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        subtable.Location.AtLocal(DataField.Payload,
                            ItemVariationDataLayout.RegionIndexesAt + (ulong)i * ItemVariationDataLayout.RegionIndexStride, i),
                        out diagnostic, subtable.RegionIndexes[i], (ulong)regionCount);
        }
        model = new(axisCount, regions.Select(static region => (VariationRegionAxis[])region.Clone()).ToArray(),
            data.ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one region's axis triples under the same value domain; width
    /// classes, row order and the outer layout never move (card 4 edit API).</summary>
    public static ReadStatus TryWithRegionAxes(VariationStoreTable model, int region,
        ReadOnlySpan<VariationRegionAxis> axes, out VariationStoreTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(model);
        if (region < 0 || region >= model.RegionCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, VariationRegionLayout.RegionCountAt),
                out diagnostic, (ulong)(long)region, (ulong)model.RegionCount);
        var regions = new VariationRegionAxis[model.RegionCount][];
        for (int i = 0; i < model.RegionCount; i++)
            regions[i] = model.Regions[i].ToArray();
        axes.CopyTo(regions[region].AsSpan());
        return TryCreate(model.AxisCount, regions, model.Data.ToArray(), out updated,
            out diagnostic, new BuildBudget(limits), model.Location);
    }

    /// <summary>Replaces one delta row's values; the row's width classes are the
    /// owning subtable's and are enforced per slot (OQ-T5-01-5 finite refusal).</summary>
    public static ReadStatus TryWithDeltaRow(VariationStoreTable model, int outer, int inner,
        ReadOnlySpan<int> deltas, out VariationStoreTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(model);
        if (outer < 0 || outer >= model.DataCount || model.Data[outer] is null)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, ItemVariationStoreLayout.DataCountAt),
                out diagnostic, (ulong)(long)outer, (ulong)model.DataCount);
        var subtable = model.Data[outer]!;
        if (inner < 0 || inner >= subtable.ItemCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                subtable.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)(long)inner, (ulong)subtable.ItemCount);
        if (deltas.Length != subtable.RegionIndexCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                subtable.Location.AtLocal(DataField.Count, ItemVariationDataLayout.RegionIndexCountAt),
                out diagnostic, (ulong)deltas.Length, (ulong)subtable.RegionIndexCount);
        var rows = new int[subtable.ItemCount][];
        for (int i = 0; i < subtable.ItemCount; i++)
            rows[i] = i == inner ? deltas.ToArray() : subtable.Rows[i].ToArray();
        ReadStatus status = ItemVariationDataTableCompiler.TryCreate(subtable.WordDeltaCount,
            subtable.RegionIndexes, rows, out var replaced, out diagnostic,
            new BuildBudget(limits), subtable.Location);
        if (status != ReadStatus.Done) return status;
        var data = new ItemVariationDataTable?[model.DataCount];
        for (int i = 0; i < model.DataCount; i++)
            data[i] = i == outer ? replaced : model.Data[i];
        return TryCreate(model.AxisCount, model.Regions.ToArray(), data, out updated,
            out diagnostic, new BuildBudget(limits), model.Location);
    }

    internal static int Measure(VariationStoreTable model)
    {
        int length = ItemVariationStoreLayout.DataOffsetsAt + ItemVariationStoreLayout.DataOffsetStride * model.DataCount;
        length += VariationRegionLayout.HeaderLength + model.RegionCount * model.AxisCount * 6;
        foreach (var subtable in model.Data)
        {
            if (subtable is null) continue;
            length += ItemVariationDataTableCompiler.Measure(subtable);
        }
        return length;
    }

    /// <summary>Root-writer emission for the store body at the slice origin: format,
    /// regionListOffset (= the declared offsets-array end), count, the per-outer
    /// offsets, the region list and every non-NULL subtable, in canonical order.</summary>
    internal static void WritePlanned(VariationStoreTable model, Span<byte> destination)
    {
        var location = model.Location;
        int dataOffsetsEnd = ItemVariationStoreLayout.DataOffsetsAt +
            ItemVariationStoreLayout.DataOffsetStride * model.DataCount;
        CoverageTableCompiler.RequireWritten(ItemVariationStoreView.TryWriteFormat(destination, 1,
            out _, location));
        // The fixup values are preflighted (the region list always follows the offset
        // array and every live subtable follows it), so any refusal here is an
        // invariant breach, matching the GDEF root writer's slot discipline.
        CoverageTableCompiler.RequireWritten(ItemVariationStoreView.TryWriteVariationRegionListOffset(
            destination, (uint)dataOffsetsEnd, out _, location));
        CoverageTableCompiler.RequireWritten(ItemVariationStoreView.TryWriteItemVariationDataCount(
            destination, (ushort)model.DataCount, out _, location));
        int bodiesAt = dataOffsetsEnd + VariationRegionLayout.HeaderLength
            + model.RegionCount * model.AxisCount * 6;
        int cursor = bodiesAt;
        for (int outer = 0; outer < model.DataCount; outer++)
        {
            var subtable = model.Data[outer];
            uint target = subtable is null ? 0u : (uint)cursor;
            CoverageTableCompiler.RequireWritten(ItemVariationStoreView.TryWriteDataOffsets(destination,
                (ulong)model.DataCount, new ItemVariationStoreView.DataOffsetsData(outer, target),
                out _, location));
            if (subtable is not null)
                cursor += ItemVariationDataTableCompiler.Measure(subtable);
        }
        var regionList = destination.Slice(dataOffsetsEnd);
        CoverageTableCompiler.RequireWritten(VariationRegionListView.TryWriteAxisCount(regionList,
            (ushort)model.AxisCount, out _, location));
        CoverageTableCompiler.RequireWritten(VariationRegionListView.TryWriteRegionCount(regionList,
            (ushort)model.RegionCount, out _, location));
        int tripleAt = VariationRegionLayout.HeaderLength;
        foreach (var region in model.Regions)
        {
            foreach (var axis in region)
            {
                global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                    regionList.Slice(tripleAt), axis.StartCoord);
                global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                    regionList.Slice(tripleAt + 2), axis.PeakCoord);
                global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                    regionList.Slice(tripleAt + 4), axis.EndCoord);
                tripleAt += 6;
            }
        }
        cursor = bodiesAt;
        foreach (var subtable in model.Data)
        {
            if (subtable is null) continue;
            ItemVariationDataTableCompiler.WritePlanned(subtable, destination.Slice(cursor));
            cursor += ItemVariationDataTableCompiler.Measure(subtable);
        }
    }
}
