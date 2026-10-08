using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One tuple variation of a GlyphVariationData block or the cvar store:
/// the raw tupleIndex (the bit classification is preserved - OQ-7), the peak
/// tuple as embedded F2DOT14 coordinates or the shared-tuple index discrimination
/// (gvar only ever materializes the embedded ones here; a cleared flag means the
/// low 12 bits index the shared tuples), the optional intermediate region, the
/// point number set and the delta columns. The point number set keeps the wire's
/// three states (card 2): a null list is the count==0 all-points/all-CVTs marker
/// (never expanded - the "raw semantics required by IUP" model face), an empty
/// list is "no point data at all" (neither private nor shared data), and a
/// non-empty list is the explicit increasing numbers. Deltas are the X then Y
/// columns for gvar and the single column for cvar (Y empty).</summary>
public sealed class TupleVariation
{
    internal TupleVariation(ushort variationDataSize, ushort tupleIndex, short[]? peakCoords,
        short[]? intermediateStartCoords, short[]? intermediateEndCoords, bool hasPrivatePoints,
        int[]? pointNumbers, short[] xDeltas, short[] yDeltas, byte[]? rawSerializedRun,
        int rawPointBytesLength, int serializedSlack)
    {
        VariationDataSize = variationDataSize;
        TupleIndex = tupleIndex;
        PeakCoords = peakCoords;
        IntermediateStartCoords = intermediateStartCoords;
        IntermediateEndCoords = intermediateEndCoords;
        HasPrivatePoints = hasPrivatePoints;
        PointNumbers = pointNumbers;
        XDeltas = xDeltas;
        YDeltas = yDeltas;
        RawSerializedRun = rawSerializedRun;
        RawPointBytesLength = rawPointBytesLength;
        SerializedSlack = serializedSlack;
    }

    /// <summary>The source's declared serialized run size (edited tuples recompute
    /// it at emission).</summary>
    public ushort VariationDataSize { get; }

    /// <summary>The raw tupleIndex: EMBEDDED_PEAK/INTERMEDIATE/PRIVATE bits and the
    /// shared index field exactly as read (the write side preserves the
    /// classification, OQ-7).</summary>
    public ushort TupleIndex { get; }

    public bool HasEmbeddedPeak => PeakCoords is not null;

    /// <summary>The embedded peak tuple's raw F2DOT14 per axis, or null when the
    /// tuple dereferences the shared tuples (cvar tuples always carry one).</summary>
    public short[]? PeakCoords { get; }

    /// <summary>With the embedded peak cleared: the shared tuples index.</summary>
    public ushort SharedTupleIndex => (ushort)(TupleIndex & TupleVariationCodec.TupleIndexMask);

    public bool HasIntermediate => IntermediateStartCoords is not null;

    /// <summary>The intermediate region start/end coordinates (both set or both
    /// null); their well-order is an observation, never enforced (evaluation
    /// domain, card 3).</summary>
    public short[]? IntermediateStartCoords { get; }
    public short[]? IntermediateEndCoords { get; }

    public bool HasPrivatePoints { get; }

    /// <summary>null = all points/all CVTs; empty = no point data; otherwise the
    /// explicit point numbers (gvar outline items, cvar CVT indices).</summary>
    public IReadOnlyList<int>? PointNumbers { get; }

    public bool AllPoints => PointNumbers is null;

    /// <summary>The X delta column (the only column of a cvar tuple).</summary>
    public IReadOnlyList<short> XDeltas { get; }

    /// <summary>The Y delta column; empty for cvar (single column, card 0).</summary>
    public IReadOnlyList<short> YDeltas { get; }

    /// <summary>Declared run size minus consumed bytes - the tuple's tail slack
    /// (an observation; the frozen corpus carries zero per-tuple pad, card 0).</summary>
    public int SerializedSlack { get; }

    /// <summary>The exact source serialized run; non-null tuples emit verbatim
    /// (OQ-7). Edited tuples carry null and repack their deltas.</summary>
    internal byte[]? RawSerializedRun { get; }

    /// <summary>Leading private point number bytes inside the raw run (0 when the
    /// tuple has no private data); the repack path preserves them byte-for-byte.</summary>
    internal int RawPointBytesLength { get; }
}

/// <summary>One owned GlyphVariationData block (gvar): the packed
/// tupleVariationCount with its shared point number flag, the shared point
/// numbers of this block (per-GVD data - there is no cross-GVD sharing) and the
/// materialized tuples in header order.</summary>
public sealed class GlyphVariationDataTable
{
    internal GlyphVariationDataTable(ushort tupleVariationCountRaw, int[]? sharedPointNumbers,
        List<TupleVariation> tuples, int blockSlack)
    {
        TupleVariationCountRaw = tupleVariationCountRaw;
        SharedPointNumbers = sharedPointNumbers;
        Tuples = tuples;
        BlockSlack = blockSlack;
    }

    /// <summary>The packed field: SHARED_POINT_NUMBERS in the high bit, reserved
    /// 0x7000 (refused when set), count in the low 12 bits.</summary>
    public ushort TupleVariationCountRaw { get; }

    public int TupleCount => TupleVariationCountRaw & TupleVariationCodec.TvcCountMask;

    public bool SharesPointNumbers =>
        (TupleVariationCountRaw & TupleVariationCodec.SharedPointNumbersFlag) != 0;

    /// <summary>null = flag clear; empty = the shared data is the all-points
    /// marker; non-empty = the explicit shared point numbers.</summary>
    public IReadOnlyList<int>? SharedPointNumbers { get; }

    public IReadOnlyList<TupleVariation> Tuples { get; }

    /// <summary>Serialized end to block end in bytes (the block-level tail pad -
    /// an observation; real pads are at most one byte, card 0).</summary>
    public int BlockSlack { get; }
}
