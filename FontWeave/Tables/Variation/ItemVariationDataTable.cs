using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned ItemVariationData subtable: every delta-set row fully decoded
/// (witness scale is bounded - card 3.2 materialization strategy), each row's values
/// in the subtable's regionIndexes order with the word deltas first (spec DeltaSet
/// record). Width classes are kept exactly as read; no widening or normalization
/// happens on the read path.</summary>
public sealed class ItemVariationDataTable
{
    internal ItemVariationDataTable(ushort itemCount, ushort wordDeltaCount, ushort[] regionIndexes,
        int[][] rows, DataLocation location, int consumedLength)
    {
        ItemCount = itemCount;
        WordDeltaCount = wordDeltaCount;
        RegionIndexes = Array.AsReadOnly(regionIndexes);
        Rows = Array.AsReadOnly(rows);
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort ItemCount { get; }
    /// <summary>The raw packed wire value: bit 0x8000 is LONG_WORDS, the low 15 bits
    /// the word-delta count (spec otvarcommonformats.md). Flag semantics live here.</summary>
    public ushort WordDeltaCount { get; }
    public bool LongWords => (WordDeltaCount & ItemVariationDataLayout.LongWordsFlag) != 0;
    /// <summary>Count of long (word) deltas per row: the masked low 15 bits.</summary>
    public int WordDeltaCountValue => WordDeltaCount & ItemVariationDataLayout.WordDeltaCountMask;
    public IReadOnlyList<ushort> RegionIndexes { get; }
    public int RegionIndexCount => RegionIndexes.Count;
    public IReadOnlyList<int[]> Rows { get; }
    public DataLocation Location { get; }
    /// <summary>The declared subtable length: header, regionIndexes array and every
    /// packed delta row, excluding any enclosing-window suffix.</summary>
    public int ConsumedLength { get; }
}
