using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned ItemVariationStore format 1: the decoded region list (raw F2Dot14
/// triples) and every non-NULL ItemVariationData subtable. The (outer, inner) row-key
/// dereference is this model's product face (card 3.2); scalar weighting of a delta
/// row against region coordinates stays oracle-only.</summary>
public sealed class VariationStoreTable
{
    private readonly ItemVariationDataTable?[] _data;
    private readonly VariationRegionAxis[][] _regions;

    internal VariationStoreTable(int axisCount, VariationRegionAxis[][] regions,
        ItemVariationDataTable?[] data, DataLocation location, int consumedLength)
    {
        AxisCount = axisCount;
        _regions = regions;
        _data = data;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public int AxisCount { get; }
    public int RegionCount => _regions.Length;
    /// <summary>Per region, one raw F2Dot14 triple per axis.</summary>
    public IReadOnlyList<VariationRegionAxis[]> Regions => Array.AsReadOnly(_regions);
    /// <summary>Per outer index, the decoded subtable; NULL is the spec-legal "no
    /// variation data" outer slot (a NULL dataOffsets entry).</summary>
    public IReadOnlyList<ItemVariationDataTable?> Data => Array.AsReadOnly(_data);
    public int DataCount => _data.Length;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>The (outer, inner) row-keyed dereference (card 3.2). The 0xFFFF/0xFFFF
    /// sentinel and a NULL outer slot resolve to the no-variation result (a spec
    /// meaning, not an error and not an unbounded read); outer or inner outside the
    /// declared domains are InvalidIndex.</summary>
    public ReadStatus TryGetDeltaSet(int outer, int inner, out bool hasVariation,
        out ReadOnlySpan<int> deltas, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        hasVariation = false;
        deltas = default;
        // The sentinel keeps its special meaning ahead of every domain check: it is
        // defined for any store shape ("functionally equivalent to ... deltas of 0").
        if (outer == 0xFFFF && inner == 0xFFFF)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (outer < 0 || outer >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, ItemVariationStoreLayout.DataCountAt),
                out diagnostic, (ulong)(long)outer, (ulong)_data.Length);
        ItemVariationDataTable? subtable = _data[outer];
        if (subtable is null)
        {
            // "There is no variation data for this outer index": a defined no-change
            // result, not a missing-table error.
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (inner < 0 || inner >= subtable.ItemCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                subtable.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                out diagnostic, (ulong)(long)inner, subtable.ItemCount);
        deltas = subtable.Rows[inner];
        hasVariation = true;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
