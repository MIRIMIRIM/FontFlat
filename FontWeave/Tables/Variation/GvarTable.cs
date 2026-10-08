using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned gvar 1.0 table: the two explicit authorities the caller
/// supplied (fvarAxisCount as the axis authority, numGlyphs from maxp), the
/// shared tuples as raw F2DOT14 vectors, the decoded GlyphVariationData offsets
/// (short-form values already doubled) and the flags-preserved offsets form. The
/// table bytes are kept so the per-glyph blocks can be materialized lazily - the
/// glyf-domain glyph-scoped shape (card 2, OQ-1): a whole-table read validates
/// every block without materializing the 70,612-tuple witnesses, and
/// <see cref="TryReadGlyphVariationData"/> materializes one glyph at a time.</summary>
public sealed class GvarTable
{
    private readonly byte[] _tableBytes;
    private readonly int[] _offsets;
    private readonly int _gvdArrayOffset;

    internal GvarTable(int axisCount, int numGlyphs, ushort flags, short[][] sharedTuples,
        int[] offsets, int gvdArrayOffset, byte[] tableBytes, DataLocation location,
        int consumedLength)
    {
        AxisCount = axisCount;
        NumGlyphs = numGlyphs;
        Flags = flags;
        SharedTuples = sharedTuples;
        _offsets = offsets;
        _gvdArrayOffset = gvdArrayOffset;
        _tableBytes = tableBytes;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The validated fvar axis count (equal to the header's AxisCount).</summary>
    public int AxisCount { get; }

    /// <summary>The validated maxp numGlyphs (equal to the header's GlyphCount).</summary>
    public int NumGlyphs { get; }

    /// <summary>The raw header flags; bit0 is the offsets form (preserved on
    /// write, OQ-7), the upper bits carry no defined semantics.</summary>
    public ushort Flags { get; }

    public bool LongOffsets => (Flags & GvarLayout.LongOffsetsFlag) != 0;

    /// <summary>The shared peak tuples, each axisCount raw F2DOT14 values in axis
    /// order (gvar-only data; cvar has none).</summary>
    public IReadOnlyList<short[]> SharedTuples { get; }

    /// <summary>The decoded byte offsets relative to the GlyphVariationData array
    /// start, glyphCount+1 entries (short-form values doubled). Equal neighbors
    /// are the absent-glyph form (a legal positive, card 3).</summary>
    public IReadOnlyList<int> GlyphVariationDataOffsets => _offsets;

    public DataLocation Location { get; }

    /// <summary>The whole table is consumed (the offsets array plus the
    /// GlyphVariationData array span the tail of the table).</summary>
    public int ConsumedLength { get; }

    /// <summary>Materializes one glyph's GlyphVariationData block.
    /// <paramref name="pointCount"/> is the glyph's outline item count from the
    /// glyf/loca typed face (simple glyphs: total points; composites: the direct
    /// component count - the four phantom points are added here, card OQ-1); a
    /// negative value means no topology context, which refuses all-points tuples
    /// with UnsupportedOperation while sparse tuples still decode. An absent
    /// glyph (zero-size block) is Done with present=false.</summary>
    public ReadStatus TryReadGlyphVariationData(int glyphId, int pointCount,
        out GlyphVariationDataTable? data, out bool present, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        data = null;
        present = false;
        var budget = new BuildBudget(limits);
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int start = _gvdArrayOffset + _offsets[glyphId];
        int size = _offsets[glyphId + 1] - _offsets[glyphId];
        if (size == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int allPointsDeltaCount = pointCount < 0 ? -1 :
            (pointCount + TupleVariationCodec.PhantomPointCount) * 2;
        var tuples = new List<TupleVariation>();
        // Whole-table coordinates (headerStart = dataOffsetBase = the block
        // start) so diagnostics land on table-absolute offsets, matching the
        // validation pass.
        ReadStatus status = TupleVariationStoreReader.ReadStore(
            _tableBytes, start, start + size, start, isCvar: false, axisCount: AxisCount,
            sharedTupleCount: SharedTuples.Count, allPointsDeltaCount: allPointsDeltaCount,
            cvtValueCount: -1, materialize: true, tuplesOut: tuples, location: Location,
            budget: budget, tvcRaw: out ushort tvcRaw, sharedPointNumbers: out int[]? shared,
            serializedEnd: out int serializedEnd, diagnostic: out diagnostic);
        if (status != ReadStatus.Done) return status;
        data = new GlyphVariationDataTable(tvcRaw, shared, tuples, start + size - serializedEnd);
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
