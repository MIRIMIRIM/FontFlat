using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Operations.Subset;

public static partial class SubsetOperation
{
    internal static ReadStatus PruneVariationStore(VariationStoreTable source,
        IReadOnlyCollection<DeltaSetIndex> liveIndices, BuildBudget budget,
        out VariationStoreTable? pruned,
        out Dictionary<DeltaSetIndex, DeltaSetIndex>? indexMap,
        out FontDiagnostic diagnostic)
    {
        pruned = null;
        indexMap = null;
        if (!budget.Work((long)liveIndices.Count + source.RegionCount,
                source.Location, out diagnostic) ||
            !budget.Buffer(8L * liveIndices.Count + 13L * source.RegionCount,
                source.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var remap = new Dictionary<DeltaSetIndex, DeltaSetIndex>();
        var used = new SortedDictionary<ushort, SortedSet<ushort>>();
        foreach (var index in liveIndices)
        {
            ReadStatus status = source.TryGetDeltaSet(index.Outer, index.Inner,
                out bool hasVariation, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!hasVariation)
            {
                remap[index] = new(ushort.MaxValue, ushort.MaxValue);
                continue;
            }
            if (!used.TryGetValue(index.Outer, out var rows))
                used.Add(index.Outer, rows = []);
            rows.Add(index.Inner);
        }
        if (!budget.Buffer(32L * used.Count, source.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var usedRegions = new bool[source.RegionCount];
        var plans = new List<(ItemVariationDataTable Source, int[] Columns, int[][] Rows, ushort Words)>(used.Count);
        foreach (var (outer, indices) in used)
        {
            var data = source.Data[outer]!;
            if (!budget.Work(((long)indices.Count + 4) * data.RegionIndexCount,
                    data.Location, out diagnostic) ||
                !budget.Buffer(5L * data.RegionIndexCount + 8L * indices.Count,
                    data.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var widths = new byte[data.RegionIndexCount];
            foreach (ushort inner in indices)
                for (int column = 0; column < widths.Length; column++)
                {
                    int value = data.Rows[inner][column];
                    byte width = value == 0 ? (byte)0 :
                        value is >= sbyte.MinValue and <= sbyte.MaxValue ? (byte)1 : (byte)2;
                    if (width > widths[column]) widths[column] = width;
                }
            int columnCount = 0, wordCount = 0;
            foreach (byte width in widths)
            {
                if (width > 0) columnCount++;
                if (width == 2) wordCount++;
            }
            var columns = new int[columnCount];
            int next = 0;
            for (int width = 2; width > 0; width--)
                for (int column = 0; column < widths.Length; column++)
                    if (widths[column] == width) columns[next++] = column;
            foreach (int column in columns) usedRegions[data.RegionIndexes[column]] = true;
            if (!budget.Work((long)indices.Count * (columns.Length + 1),
                    data.Location, out diagnostic) ||
                !budget.Buffer(4L * indices.Count * columns.Length,
                    data.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            int outputOuter = plans.Count, outputInner = 0;
            var selectedRows = new int[indices.Count][];
            foreach (ushort inner in indices)
            {
                var row = new int[columns.Length];
                for (int column = 0; column < columns.Length; column++)
                    row[column] = data.Rows[inner][columns[column]];
                selectedRows[outputInner] = row;
                remap[new(outer, inner)] = new((ushort)outputOuter, (ushort)outputInner);
                outputInner++;
            }
            plans.Add((data, columns, selectedRows, (ushort)wordCount));
        }
        var regions = new List<VariationRegionAxis[]>(source.RegionCount);
        var regionMap = new int[source.RegionCount];
        for (int region = 0; region < usedRegions.Length; region++)
            if (usedRegions[region])
            {
                regionMap[region] = regions.Count;
                regions.Add(source.Regions[region]);
            }
        if (!budget.Buffer(8L * plans.Count, source.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var tables = new ItemVariationDataTable?[plans.Count];
        for (int outer = 0; outer < plans.Count; outer++)
        {
            var plan = plans[outer];
            if (!budget.Work(plan.Columns.Length, plan.Source.Location, out diagnostic) ||
                !budget.Buffer(2L * plan.Columns.Length, plan.Source.Location,
                    out diagnostic))
                return ReadStatus.ResourceLimit;
            var regionIndices = new ushort[plan.Columns.Length];
            for (int column = 0; column < regionIndices.Length; column++)
                regionIndices[column] = (ushort)regionMap[plan.Source.RegionIndexes[plan.Columns[column]]];
            ReadStatus status = ItemVariationDataTableCompiler.TryCreate(plan.Words,
                regionIndices, plan.Rows, out tables[outer], out diagnostic,
                budget, plan.Source.Location);
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus result = VariationStoreTableCompiler.TryCreate(source.AxisCount,
            regions, tables, out pruned, out diagnostic, budget, source.Location);
        if (result == ReadStatus.Done) indexMap = remap;
        return result;
    }

    private static ReadStatus PruneMetricVariationStore(VariationStoreTable source,
        DeltaSetIndexMapTable?[] maps, BuildBudget budget,
        out VariationStoreTable? pruned, out FontDiagnostic diagnostic)
    {
        pruned = null;
        var live = new HashSet<DeltaSetIndex>();
        foreach (var map in maps)
        {
            if (map is null) continue;
            if (!budget.Work(map.MapCount, map.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            for (int i = 0; i < map.MapCount; i++)
            {
                if (!map.TryGetMapping(i, out var index))
                    throw new InvalidOperationException();
                live.Add(index);
            }
        }
        ReadStatus status = PruneVariationStore(source, live, budget,
            out pruned, out var indexMap, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] is not { } map) continue;
            if (!budget.Buffer(4L * map.MapCount, map.Location, out diagnostic) ||
                !budget.Work(map.MapCount, map.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var entries = new DeltaSetIndex[map.MapCount];
            ushort maxInner = 0, maxOuter = 0;
            for (int entry = 0; entry < entries.Length; entry++)
            {
                if (!map.TryGetMapping(entry, out var original))
                    throw new InvalidOperationException();
                var replacement = indexMap![original];
                entries[entry] = replacement;
                maxInner = System.Math.Max(maxInner, replacement.Inner);
                maxOuter = System.Math.Max(maxOuter, replacement.Outer);
            }
            int innerBits = 1;
            while (maxInner >= (1u << innerBits)) innerBits++;
            uint maximum = ((uint)maxOuter << innerBits) | maxInner;
            int entrySize = 1;
            while (maximum >= (1UL << (8 * entrySize))) entrySize++;
            byte format = (byte)(((entrySize - 1) << 4) | (innerBits - 1));
            status = DeltaSetIndexMapTableCompiler.TryCreate(format, entries,
                out maps[i], out diagnostic, budget, map.Location);
            if (status != ReadStatus.Done) return status;
        }
        return ReadStatus.Done;
    }
}
