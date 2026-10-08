using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned cvar 1.0 table: the whole-table materialization of the one
/// versioned tuple variation store (the frozen witnesses are bounded at 283
/// bytes, card 2). The two explicit authorities the caller supplied are kept as
/// context: the fvar axis count (the TVH stride authority) and the CVT value
/// count that bounds every CVT index (T0). Tuples always carry embedded peaks
/// (mandatory, OQ-4) and a single delta column.</summary>
public sealed class CvarTable
{
    internal CvarTable(int axisCount, int cvtValueCount, ushort tupleVariationCountRaw,
        int[]? sharedPointNumbers, List<TupleVariation> tuples, DataLocation location,
        int consumedLength)
    {
        AxisCount = axisCount;
        CvtValueCount = cvtValueCount;
        TupleVariationCountRaw = tupleVariationCountRaw;
        SharedPointNumbers = sharedPointNumbers;
        Tuples = tuples;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public int AxisCount { get; }

    /// <summary>The explicit CVT value count the CVT indices are bounded by
    /// (floor(cvtLength/2) from CvtView.ValueCount).</summary>
    public int CvtValueCount { get; }

    /// <summary>The packed field: SHARED_POINT_NUMBERS in the high bit, reserved
    /// 0x7000 (refused when set), count in the low 12 bits.</summary>
    public ushort TupleVariationCountRaw { get; }

    public int TupleCount => TupleVariationCountRaw & TupleVariationCodec.TvcCountMask;

    public bool SharesPointNumbers =>
        (TupleVariationCountRaw & TupleVariationCodec.SharedPointNumbersFlag) != 0;

    /// <summary>null = flag clear; empty = the shared data is the all-CVTs
    /// marker; non-empty = the explicit shared CVT indices.</summary>
    public IReadOnlyList<int>? SharedPointNumbers { get; }

    public IReadOnlyList<TupleVariation> Tuples { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
