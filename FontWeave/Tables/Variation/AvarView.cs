using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of avar 1.0 (T5-02). The header and
/// version gate are declared; this partial owns the reserved-zero domain, the
/// segment-map walk (the card's hand-written boundary: positionMapCount plus that
/// many 4-byte F2DOT14 pairs per axis) and the explicit axis-count contract -
/// fvarAxisCount is the caller's fvar-derived expectation, and a mismatch refuses
/// exactly like the T5-01 region-list contract (card 1.2-III: no second, implicit
/// cross-table check exists). The tolerance domain is deliberately not validated
/// here: missing required pairs and non-monotonic records are spec-defined behavior
/// and travel to the observation face (card 1.2-I).</summary>
public readonly ref partial struct AvarView
{
    /// <summary>The strict structural pass: header domains, the explicit axis-count
    /// contract and the full map extent (a truncated map refuses; a tolerated record
    /// never does).</summary>
    public ReadStatus ValidateTable(int fvarAxisCount, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(fvarAxisCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int fvarAxisCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetReserved(out ushort reserved, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, AvarLayout.ReservedAt),
                out diagnostic, reserved, 0);
        if (fvarAxisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)fvarAxisCount), 0);
        status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount != fvarAxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, AvarLayout.AxisCountAt),
                out diagnostic, axisCount, (ulong)fvarAxisCount);
        return ValidateMaps(axisCount, out diagnostic, budget);
    }

    // The map extent: per axis a positionMapCount and 4*count bytes of pairs; only
    // truncation refuses. An empty map (positionMapCount 0, the axis is not
    // modified) is spec-legal and has no real witness (frozen facts R7) - carried
    // by synthetic wire (card 3.1).
    private ReadStatus ValidateMaps(int axisCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        diagnostic = default;
        int position = AvarLayout.HeaderLength;
        for (int axis = 0; axis < axisCount; axis++)
        {
            if (!InBounds(position, AvarLayout.SegmentMapCountAt))
                return Truncated(position, AvarLayout.SegmentMapCountAt, axis, out diagnostic);
            if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            ushort mapCount = ReadWire.U16(_data, position);
            position += AvarLayout.SegmentMapFirstPairAt;
            long pairBytes = (long)mapCount * AvarLayout.SegmentPairStride;
            if (!budget.Work(pairBytes, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            if (!InBounds(position, pairBytes))
                return Truncated(position, AvarLayout.SegmentPairStride, axis, out diagnostic);
            position += (int)pairBytes;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private bool InBounds(int position, long width) =>
        position >= 0 && width <= (long)_data.Length - position;

    private ReadStatus Truncated(int position, int width, int axis, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            Location.AtLocal(DataField.Count, (ulong)position, axis),
            out diagnostic, (ulong)_data.Length, (ulong)(position + width));

    /// <summary>Reads the complete table into an owned <see cref="AvarTable"/>: every
    /// axis's raw pairs plus its tolerance observation (card 1.2-I), fully
    /// materialized - the witness bound is 9 map rows over 13 axes (frozen facts R8),
    /// and per-axis laziness stays available to a real need (the T5-01 OQ-6
    /// registration).</summary>
    public ReadStatus TryReadTable(int fvarAxisCount, out AvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(fvarAxisCount, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int fvarAxisCount, out AvarTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(fvarAxisCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var maps = new (AvarMapPair[], AvarMapObservation)[axisCount];
        int position = AvarLayout.HeaderLength;
        for (int axis = 0; axis < axisCount; axis++)
        {
            ushort mapCount = ReadWire.U16(_data, position);
            position += AvarLayout.SegmentMapFirstPairAt;
            var pairs = new AvarMapPair[mapCount];
            for (int i = 0; i < mapCount; i++)
            {
                pairs[i] = new AvarMapPair(
                    VariationWire.F2Dot14(_data, position),
                    VariationWire.F2Dot14(_data, position + 2));
                position += AvarLayout.SegmentPairStride;
            }
            maps[axis] = (pairs, Observe(pairs));
        }
        model = new(maps, Location, Math.Max(position, AvarLayout.HeaderLength));
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The observation pass: required pairs over the raw records - the from values
    // -1/0/+1 in F2DOT14 representation (-16384/0/16384); ignored records through
    // the evaluator's kept view. (The corpus probe caught the first draft comparing
    // the integral values against raw pairs - exactly the class of drift the
    // frozen-facts reconciliation exists to catch.)
    private static AvarMapObservation Observe(AvarMapPair[] pairs)
    {
        bool requiredMissing = !ContainsFrom(pairs, -16384) || !ContainsFrom(pairs, 0) ||
            !ContainsFrom(pairs, 16384);
        int ignored = pairs.Length - AvarTable.KeepConforming(pairs).Length;
        return new(requiredMissing, ignored);
    }

    private static bool ContainsFrom(AvarMapPair[] pairs, int from)
    {
        foreach (AvarMapPair pair in pairs)
            if (pair.From == from) return true;
        return false;
    }
}
