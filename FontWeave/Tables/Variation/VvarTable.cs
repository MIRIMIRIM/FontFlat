using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Which VVAR vertical metric a delta-set index is requested for. The three
/// side/origin maps are optional and a NULL map is this metric's no-variation answer
/// (OQ-T5-04-4: the VVAR chapter carries no lsb/rsb-style fallback text and the
/// HVAR-specific advance-index fallback is not inherited) - only the advance map
/// NULL has the spec's implicit (0, gid) reading (vvar.md L73, "as in the HVAR
/// table").</summary>
public enum VvarMetric
{
    AdvanceHeight = 0,
    Tsb = 1,
    Bsb = 2,
    VOrg = 3,
}

/// <summary>Owned VVAR 1.0 table: the item variation store plus the four optional
/// DeltaSetIndexMaps, with the spec's gid-to-delta-set-index semantics as the
/// product face (card 3) - implicit (0, gid) mapping when the advance map is NULL,
/// the last-entry fallback for gid greater or equal mapCount, the 0xFFFF/0xFFFF
/// sentinel as the no-variation result, and tsb/bsb/vOrg NULL as the no-variation
/// answer for their metric (the deliberate twin divergence from HVAR, OQ-T5-04-4).
/// The API is row-keyed: callers get (outer, inner) keys and delta rows; converting
/// a delta row into an instance value (region scalars, vmtx/VORG rewriting) stays
/// outside the product (T0 T5-04).</summary>
public sealed class VvarTable
{
    internal VvarTable(VariationStoreTable store, DeltaSetIndexMapTable? advanceHeightMap,
        DeltaSetIndexMapTable? tsbMap, DeltaSetIndexMapTable? bsbMap, DeltaSetIndexMapTable? vOrgMap,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        Store = store;
        AdvanceHeightMapping = advanceHeightMap;
        TsbMapping = tsbMap;
        BsbMapping = bsbMap;
        VOrgMapping = vOrgMap;
        NumGlyphs = numGlyphs;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public VariationStoreTable Store { get; }
    public DeltaSetIndexMapTable? AdvanceHeightMapping { get; }
    public DeltaSetIndexMapTable? TsbMapping { get; }
    public DeltaSetIndexMapTable? BsbMapping { get; }
    public DeltaSetIndexMapTable? VOrgMapping { get; }
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>Resolves the metric's delta-set index for one glyph. A glyph outside
    /// the glyph-count context is InvalidIndex; the sentinel keeps its no-variation
    /// meaning exactly as mapped.</summary>
    public ReadStatus TryGetDeltaSetIndex(VvarMetric metric, int gid, out DeltaSetIndex index,
        out bool hasVariation, out FontDiagnostic diagnostic)
    {
        index = default;
        hasVariation = false;
        if (gid < 0 || gid >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)gid), (ulong)NumGlyphs);
        DeltaSetIndexMapTable? map = metric switch
        {
            VvarMetric.AdvanceHeight => AdvanceHeightMapping,
            VvarMetric.Tsb => TsbMapping,
            VvarMetric.Bsb => BsbMapping,
            _ => VOrgMapping,
        };
        if (map is null)
        {
            if (metric != VvarMetric.AdvanceHeight)
            {
                // OQ-T5-04-4: tsb/bsb/vOrg NULL = the metric carries no variation -
                // a defined answer, never a fallback to the advance index.
                diagnostic = default;
                return ReadStatus.Done;
            }
            // Implicit mapping: outer level zero, the glyph ID as the inner level
            // (spec vvar.md L73, "as in the HVAR table"). The inner domain against
            // VarData[0] was proven at validation; the store dereference keeps its
            // own bounds check.
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
    public ReadStatus TryGetDeltaSets(VvarMetric metric, int gid, out bool hasVariation,
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
