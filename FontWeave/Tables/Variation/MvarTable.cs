using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One MVAR 1.0 ValueRecord: the opaque value tag (a u32 key only - the
/// 39-tag registry and its case/private-use rules are the consumption domain, card
/// 11) plus the (outer, inner) row key into the item variation store. The 0xFFFF/
/// 0xFFFF pair is the spec's no-variation-data sentinel and travels as a normal
/// value (HubotSans carries three real sentinel records).</summary>
public readonly record struct MvarValueRecord(uint ValueTag, ushort Outer, ushort Inner);

/// <summary>Owned MVAR 1.0 table: the records in wire order (the declared
/// valueRecordSize stride is preserved as read) plus the item variation store when
/// the record list is non-empty - the L60 coupling makes a record-less table the
/// only store-less shape. The product face is the tag-keyed dereference (card 3):
/// a tag absent from the records is the spec's "constant across the font's
/// variation space" answer (L79), the sentinel resolves to no variation ahead of
/// every domain check, and a record's (outer, inner) domain is checked at
/// dereference time exactly like the store's own contract (OQ-T5-04-6; no
/// validation-time full scan). Converting a delta row into a rewritten static
/// metric (OS/2, hhea, post...) stays outside the product (card 11).</summary>
public sealed class MvarTable
{
    private readonly MvarValueRecord[] _records;

    internal MvarTable(VariationStoreTable? store, MvarValueRecord[] records,
        ushort valueRecordSize, DataLocation location, int consumedLength)
    {
        Store = store;
        _records = records;
        ValueRecordSize = valueRecordSize;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The item variation store; NULL exactly when the table declares zero
    /// records (the L60 count/offset coupling).</summary>
    public VariationStoreTable? Store { get; }
    public IReadOnlyList<MvarValueRecord> Records => Array.AsReadOnly(_records);
    public int RecordCount => _records.Length;
    /// <summary>The declared per-record stride, preserved verbatim - not pinned to
    /// the 1.0 corpus constant (OQ-T5-04-1 lean).</summary>
    public ushort ValueRecordSize { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>Resolves the value tag's delta-set index by binary search over the
    /// strictly ordered records. An absent tag is the defined constant answer, not
    /// an error; the sentinel keeps its no-variation meaning exactly as recorded.</summary>
    public ReadStatus TryGetDeltaSetIndex(uint valueTag, out DeltaSetIndex index,
        out bool hasVariation, out FontDiagnostic diagnostic)
    {
        index = default;
        hasVariation = false;
        int low = 0, high = _records.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            uint candidate = _records[middle].ValueTag;
            if (candidate == valueTag)
            {
                index = new(_records[middle].Outer, _records[middle].Inner);
                if (index.IsNoVariationData)
                {
                    hasVariation = false;
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                hasVariation = true;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (candidate < valueTag) low = middle + 1;
            else high = middle - 1;
        }
        // "If the tag does not occur ... the item is constant across the font's
        // variation space" (spec L79): a defined no-change result.
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One-call dereference: tag resolution plus the store row. The returned
    /// row is the decoded delta sequence in the owning subtable's regionIndexes
    /// order; weighting it against region coordinates is oracle-only.</summary>
    public ReadStatus TryGetDeltaSets(uint valueTag, out bool hasVariation,
        out ReadOnlySpan<int> deltas, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        hasVariation = false;
        deltas = default;
        ReadStatus status = TryGetDeltaSetIndex(valueTag, out var index, out hasVariation,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!hasVariation)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return Store!.TryGetDeltaSet(index.Outer, index.Inner, out hasVariation, out deltas,
            out diagnostic, limits);
    }
}
