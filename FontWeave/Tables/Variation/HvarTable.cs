using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Which HVAR metric a delta-set index is requested for. The lsb/rsb maps are
/// optional and a NULL map reuses the advance index (OQ-T5-01-2: the 1.8/1.9 text and
/// the HarfBuzz behavior; the primary witnesses' side-bearing deltas only exist under
/// this reading).</summary>
public enum HvarMetric
{
    Advance = 0,
    Lsb = 1,
    Rsb = 2,
}

/// <summary>Owned HVAR 1.0 table: the item variation store plus the three optional
/// DeltaSetIndexMaps, with the spec's gid-to-delta-set-index semantics as the product
/// face (card 3.2) - implicit (0, gid) mapping when the advance map is NULL, the
/// last-entry fallback for gid greater or equal mapCount, the 0xFFFF/0xFFFF sentinel
/// as the no-variation result, and lsb/rsb NULL falling back to the advance index.
/// The API is row-keyed: callers get (outer, inner) keys and delta rows; converting a
/// delta row into an instance value (region scalars, hmtx rewriting) stays outside
/// the product (T0 T5-01).</summary>
public sealed class HvarTable
{
    internal HvarTable(VariationStoreTable store, DeltaSetIndexMapTable? advanceMap,
        DeltaSetIndexMapTable? lsbMap, DeltaSetIndexMapTable? rsbMap, int numGlyphs,
        DataLocation location, int consumedLength)
    {
        Store = store;
        AdvanceWidthMapping = advanceMap;
        LsbMapping = lsbMap;
        RsbMapping = rsbMap;
        NumGlyphs = numGlyphs;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public VariationStoreTable Store { get; }
    public DeltaSetIndexMapTable? AdvanceWidthMapping { get; }
    public DeltaSetIndexMapTable? LsbMapping { get; }
    public DeltaSetIndexMapTable? RsbMapping { get; }
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>Resolves the metric's delta-set index for one glyph. A glyph outside
    /// the glyph-count context is InvalidIndex; the sentinel keeps its no-variation
    /// meaning exactly as mapped.</summary>
    public ReadStatus TryGetDeltaSetIndex(HvarMetric metric, int gid, out DeltaSetIndex index,
        out bool hasVariation, out FontDiagnostic diagnostic)
    {
        index = default;
        hasVariation = false;
        if (gid < 0 || gid >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                (ulong)(long)gid, (ulong)NumGlyphs);
        DeltaSetIndexMapTable? map = metric switch
        {
            HvarMetric.Advance => AdvanceWidthMapping,
            HvarMetric.Lsb => LsbMapping ?? AdvanceWidthMapping,
            _ => RsbMapping ?? AdvanceWidthMapping,
        };
        if (map is null)
        {
            // Implicit mapping: outer level zero, the glyph ID as the inner level
            // (spec hvar.md). The inner domain against VarData[0] was proven at
            // validation; the store dereference keeps its own bounds check.
            index = new(0, (ushort)gid);
        }
        else if (!map.TryGetMapping(gid, out index))
        {
            // A non-NULL map always has at least one entry (OQ-T5-01-3 is pinned at
            // validation); this is the defensive remainder.
            hasVariation = false;
            diagnostic = default;
            return ReadStatus.Done;
        }
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

    /// <summary>One-call dereference: metric index resolution plus the store row. The
    /// returned row is the decoded delta sequence in the owning subtable's
    /// regionIndexes order; weighting it against region coordinates is oracle-only.</summary>
    public ReadStatus TryGetDeltaSets(HvarMetric metric, int gid, out bool hasVariation,
        out ReadOnlySpan<int> deltas, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        hasVariation = false;
        deltas = default;
        ReadStatus status = TryGetDeltaSetIndex(metric, gid, out var index, out hasVariation,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!hasVariation)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return Store.TryGetDeltaSet(index.Outer, index.Inner, out hasVariation, out deltas,
            out diagnostic, limits);
    }
}
