using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Which HVAR metric a delta-set index is requested for. Only the advance-width
/// map is optional; a NULL advance map means the implicit (0, gid) mapping. The lsb/rsb
/// maps are optional variation data, and a NULL one means the metric carries no HVAR
/// variation (spec hvar.md; VVAR's tsb/bsb/vOrg slots answer the same way).</summary>
public enum HvarMetric
{
    Advance = 0,
    Lsb = 1,
    Rsb = 2,
}

/// <summary>Owned HVAR 1.0 table: the item variation store plus the three optional
/// DeltaSetIndexMaps, with the spec's gid-to-delta-set-index semantics as the product
/// face (card 3.2) - implicit (0, gid) mapping when the advance map is NULL, the
/// last-entry fallback for gid greater or equal mapCount, and the 0xFFFF/0xFFFF sentinel
/// as the no-variation result. A NULL lsb/rsb map is the metric's no-variation answer:
/// the spec defines the implicit mapping for advance width only, and neither side
/// bearing carries data without its own mapping.
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
            HvarMetric.Lsb => LsbMapping,
            _ => RsbMapping,
        };
        if (map is null)
        {
            if (metric != HvarMetric.Advance)
            {
                // The spec defines the implicit (0, gid) reading for advance width
                // alone; a NULL lsb/rsb map is the metric's no-variation answer, never
                // a fallback to the advance index.
                diagnostic = default;
                return ReadStatus.Done;
            }
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

    /// <summary>Replaces one region's axis triples through the store owner; the row
    /// layout, width classes and maps never move (card 4 edit API). The glyph-count
    /// context and source identity are preserved.</summary>
    public ReadStatus TryWithRegionAxes(int region, ReadOnlySpan<VariationRegionAxis> axes,
        out HvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = VariationStoreTableCompiler.TryWithRegionAxes(Store, region, axes,
            out var store, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        return Rebuild(store!, AdvanceWidthMapping, LsbMapping, RsbMapping, out updated,
            out diagnostic, limits);
    }

    /// <summary>Replaces one delta row's values; the owning subtable's width classes
    /// are kept and their domains enforced per slot (OQ-T5-01-5 finite refusal).</summary>
    public ReadStatus TryWithDeltaRow(int outer, int inner, ReadOnlySpan<int> deltas,
        out HvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = VariationStoreTableCompiler.TryWithDeltaRow(Store, outer, inner,
            deltas, out var store, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        return Rebuild(store!, AdvanceWidthMapping, LsbMapping, RsbMapping, out updated,
            out diagnostic, limits);
    }

    /// <summary>Replaces one logical map entry; the target (outer, inner) pair must be
    /// representable in the map's own entryFormat (card 4 edit API, OQ-T5-01-5).</summary>
    public ReadStatus TryWithMapping(HvarMetric metric, int entryIndex, DeltaSetIndex entry,
        out HvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        DeltaSetIndexMapTable? map = metric switch
        {
            HvarMetric.Advance => AdvanceWidthMapping,
            HvarMetric.Lsb => LsbMapping,
            _ => RsbMapping,
        };
        if (map is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, metric switch
                {
                    HvarMetric.Advance => HvarLayout.AdvanceMapAt,
                    HvarMetric.Lsb => HvarLayout.LsbMapAt,
                    _ => HvarLayout.RsbMapAt,
                }), out diagnostic, (ulong)metric, 0);
        ReadStatus status = DeltaSetIndexMapTableCompiler.TryWithMapping(map, entryIndex, entry,
            out var replaced, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        return Rebuild(Store, metric switch
        {
            HvarMetric.Advance => replaced,
            _ => AdvanceWidthMapping,
        }, metric switch
        {
            HvarMetric.Lsb => replaced,
            _ => LsbMapping,
        }, metric switch
        {
            HvarMetric.Rsb => replaced,
            _ => RsbMapping,
        }, out updated, out diagnostic, limits);
    }

    // Re-roots the edited children under the same glyph-count context and source
    // identity through the factory's guards - the root itself is not rebuilt.
    private ReadStatus Rebuild(VariationStoreTable store, DeltaSetIndexMapTable? advanceMap,
        DeltaSetIndexMapTable? lsbMap, DeltaSetIndexMapTable? rsbMap, out HvarTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        HvarTableCompiler.TryCreate(store, advanceMap, lsbMap, rsbMap, Store.AxisCount,
            NumGlyphs, out updated, out diagnostic, limits, Location);
}
