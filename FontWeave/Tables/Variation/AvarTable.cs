using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One raw segment-map record: the F2DOT14 from/to pair exactly as stored
/// (card 3.2 keeps every value raw).</summary>
public readonly record struct AvarMapPair(int From, int To);

/// <summary>The observation face of one axis's segment map (card 1.2-I): the spec's
/// tolerance domain is defined behavior, not damage, so it is reported, never
/// refused. RequiredPairsMissing is the "no modification will be made for that
/// axis" reading (any of the -1/0/+1 from-values absent from the raw records);
/// IgnoredRecords counts records a conforming evaluator drops (a from less than or
/// equal to its predecessor's kept from, or a to below its predecessor's kept to).
/// The real corpus is fully conforming (frozen facts R7: zero violations) - the
/// tolerance domain is carried by synthetic wire in the gate.</summary>
public sealed class AvarMapObservation
{
    internal AvarMapObservation(bool requiredPairsMissing, int ignoredRecords)
    {
        RequiredPairsMissing = requiredPairsMissing;
        IgnoredRecords = ignoredRecords;
    }

    public bool RequiredPairsMissing { get; }
    public int IgnoredRecords { get; }
}

/// <summary>Owned avar 1.0 table: the per-axis segment maps in axis order, each with
/// its raw pairs and tolerance observation. The mapping evaluator
/// (<see cref="TryMapNormalized"/>) is the card's OQ-T5-02-1 product face: a
/// table-local segment-wise linear lookup over the kept records with the tolerance
/// semantics applied (missing required pairs or an empty map mean the axis does not
/// modify values). The fvar user-to-normalized conversion stays outside the product
/// (oracle-only, T0 discipline) - this method's input and output are already
/// normalized F2DOT14 raw values.</summary>
public sealed class AvarTable
{
    internal AvarTable(IReadOnlyList<(AvarMapPair[] Pairs, AvarMapObservation Observation)> maps,
        DataLocation location, int consumedLength)
    {
        Maps = maps;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The axis count this table modifies; the explicit fvar input contract
    /// has pinned it to the fvar axis count at validation.</summary>
    public int AxisCount => Maps.Count;

    public IReadOnlyList<(AvarMapPair[] Pairs, AvarMapObservation Observation)> Maps { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }

    /// <summary>Replaces one raw map pair, keeping the axis count and the map's row
    /// count (card 4: no row insertion or removal, OQ-T5-02-6). The write-side shape
    /// invariant applies to the edited target map: from strictly increasing and to
    /// non-decreasing - a violation is a finite refusal, symmetric with the read
    /// side's tolerance observation. The tolerance observation is recomputed.</summary>
    public ReadStatus TryWithMapPair(int axisIndex, int pairIndex, int from, int to,
        out AvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (axisIndex < 0 || axisIndex >= Maps.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, AvarLayout.AxisCountAt, axisIndex),
                out diagnostic, unchecked((ulong)(long)axisIndex), (ulong)Maps.Count);
        AvarMapPair[] pairs = Maps[axisIndex].Pairs;
        if (pairIndex < 0 || pairIndex >= pairs.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count,
                    (ulong)(AvarLayout.HeaderLength +
                        MapStart(axisIndex) + AvarLayout.SegmentMapFirstPairAt +
                        pairIndex * AvarLayout.SegmentPairStride), pairIndex),
                out diagnostic, unchecked((ulong)(long)pairIndex), (ulong)pairs.Length);
        var edited = (AvarMapPair[])pairs.Clone();
        edited[pairIndex] = new(from, to);
        if (!IsConforming(edited))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload,
                    (ulong)(AvarLayout.HeaderLength + MapStart(axisIndex) +
                        AvarLayout.SegmentMapFirstPairAt + pairIndex * AvarLayout.SegmentPairStride),
                    pairIndex), out diagnostic, unchecked((ulong)(long)to), unchecked((ulong)(long)from));
        var maps = new AvarMapPair[Maps.Count][];
        for (int i = 0; i < Maps.Count; i++)
            maps[i] = i == axisIndex ? edited : Maps[i].Pairs;
        return AvarTableCompiler.TryCreate(maps, out updated, out diagnostic, limits, Location);
    }

    private long MapStart(int axisIndex)
    {
        long position = 0;
        for (int i = 0; i < axisIndex; i++)
            position += AvarLayout.SegmentMapFirstPairAt +
                (long)Maps[i].Pairs.Length * AvarLayout.SegmentPairStride;
        return position;
    }

    /// <summary>The write-side map shape: from strictly increasing, to non-decreasing
    /// (card 4's "target map keeps its shape"; the read side observes, the write side
    /// refuses).</summary>
    internal static bool IsConforming(AvarMapPair[] pairs)
    {
        for (int i = 1; i < pairs.Length; i++)
        {
            if (pairs[i].From <= pairs[i - 1].From || pairs[i].To < pairs[i - 1].To)
                return false;
        }
        return true;
    }

    /// <summary>Maps one already-normalized coordinate through the axis's segment
    /// map. Identity (mapped == value, modified = false) when the axis carries no
    /// effective modification: an empty map or a map missing the required -1/0/+1
    /// pairs (card 1.2-I). Otherwise the kept records - strictly increasing from,
    /// non-decreasing to - bracket the value and the interpolation interpolates
    /// linearly with round-half-away-from-zero integer arithmetic. Values outside
    /// the kept from-range clamp to the end to-values (the spec's segment map is
    /// defined over the whole [-1,1] domain through its required pairs).</summary>
    public ReadStatus TryMapNormalized(int axisIndex, int value, out int mapped,
        out bool modified, out FontDiagnostic diagnostic)
    {
        mapped = value;
        modified = false;
        if (axisIndex < 0 || axisIndex >= Maps.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, AvarLayout.AxisCountAt, axisIndex),
                out diagnostic, unchecked((ulong)(long)axisIndex), (ulong)Maps.Count);
        (AvarMapPair[] pairs, AvarMapObservation observation) = Maps[axisIndex];
        if (pairs.Length == 0 || observation.RequiredPairsMissing)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        AvarMapPair[] kept = KeepConforming(pairs);
        if (kept.Length == 0)
        {
            // Defensive remainder: every record dropped leaves no modification.
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (value <= kept[0].From)
        {
            mapped = kept[0].To;
        }
        else if (value >= kept[^1].From)
        {
            mapped = kept[^1].To;
        }
        else
        {
            int upper = 1;
            while (kept[upper].From < value) upper++;
            AvarMapPair lower = kept[upper - 1], high = kept[upper];
            if (lower.From == high.From || lower.To == high.To)
            {
                mapped = lower.From == high.From ? high.To : lower.To;
            }
            else
            {
                long numerator = (long)(value - lower.From) * (high.To - lower.To);
                long denominator = high.From - lower.From;
                mapped = lower.To + (int)RoundHalfAway(numerator, denominator);
            }
        }
        modified = mapped != value;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The evaluator's kept view of a raw map: drops every record whose
    /// from is not strictly greater than the previous kept from, or whose to is
    /// below the previous kept to (the two "may be ignored" readings, spec avar.md).</summary>
    internal static AvarMapPair[] KeepConforming(AvarMapPair[] pairs)
    {
        var kept = new List<AvarMapPair>(pairs.Length);
        foreach (AvarMapPair pair in pairs)
        {
            if (kept.Count > 0)
            {
                AvarMapPair previous = kept[^1];
                if (pair.From <= previous.From || pair.To < previous.To) continue;
            }
            kept.Add(pair);
        }
        return kept.ToArray();
    }

    private static long RoundHalfAway(long numerator, long denominator)
    {
        long quotient = numerator >= 0
            ? (numerator + denominator / 2) / denominator
            : -((-numerator + denominator / 2) / denominator);
        return quotient;
    }
}
