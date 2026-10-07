using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of one STAT axis value table (T5-02). The format gate
/// and the fixed-length bodies are declared; this partial owns the truncation domain
/// of each body, the axisIndex domain against the caller's designAxisCount context
/// ("must be less than designAxisCount", spec STAT.md), the valueNameID wire domain
/// (card 1.2-II, OQ-T5-02-2) and the format 4 tail: a zero combinationAxisCount and
/// a repeated axisIndex refuse ("Each AxisValue record must have a different
/// axisIndex value", spec STAT.md). The STAT-version gate for format 4 (1.2 adds
/// support) lives in StatView, which owns both contexts (card 3.1).</summary>
public readonly ref partial struct StatAxisValueView
{
    public ReadStatus Validate(int designAxisCount, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(designAxisCount, out diagnostic, budget);
    }

    internal ReadStatus Validate(int designAxisCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out ulong format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (format)
        {
            case 1:
                return ValidateSingleAxis(1, designAxisCount, out diagnostic, budget);
            case 2:
                return ValidateSingleAxis(2, designAxisCount, out diagnostic, budget);
            case 3:
                return ValidateSingleAxis(3, designAxisCount, out diagnostic, budget);
            default:
                return ValidateCombination(designAxisCount, out diagnostic, budget);
        }
    }

    // Formats 1-3: one fixed-length body record truncates or carries the single-axis
    // face; the axis index and the value name ID domains close the pass.
    private ReadStatus ValidateSingleAxis(int format, int designAxisCount,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        int at = StatAxisValueLayout.PrefixLength;
        ReadStatus status;
        ushort axisIndex, valueNameID;
        switch (format)
        {
            case 1:
                status = TryReadFormat1BodyAt((ulong)at, -1, out var body1, out diagnostic);
                if (status != ReadStatus.Done) return status;
                (axisIndex, valueNameID) = (body1.AxisIndex, body1.ValueNameID);
                break;
            case 2:
                status = TryReadFormat2BodyAt((ulong)at, -1, out var body2, out diagnostic);
                if (status != ReadStatus.Done) return status;
                (axisIndex, valueNameID) = (body2.AxisIndex, body2.ValueNameID);
                break;
            default:
                status = TryReadFormat3BodyAt((ulong)at, -1, out var body3, out diagnostic);
                if (status != ReadStatus.Done) return status;
                (axisIndex, valueNameID) = (body3.AxisIndex, body3.ValueNameID);
                break;
        }
        if (!budget.Work(StatAxisValueLayout.PrefixLength +
            (format == 2 ? StatAxisValueLayout.Format2BodyStride
            : format == 3 ? StatAxisValueLayout.Format3BodyStride
            : StatAxisValueLayout.Format1BodyStride), Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        status = ValidateAxisIndex(axisIndex, designAxisCount, at, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ValidateValueNameID(valueNameID, at + 4, out diagnostic);
    }

    // Format 4: the declared header plus the dynamic tail walk (the hand-written
    // boundary). Zero combination count and duplicate entry axis indices refuse;
    // every entry's axis index stays inside the design-axis context.
    private ReadStatus ValidateCombination(int designAxisCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        int at = StatAxisValueLayout.PrefixLength;
        ReadStatus status = TryReadFormat4HeaderAt((ulong)at, -1, out var header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (header.CombinationAxisCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, (ulong)at),
                out diagnostic, 0, 1);
        if (!budget.Work((long)header.CombinationAxisCount * StatAxisValueLayout.Format4EntryStride,
            Location, out diagnostic)) return ReadStatus.ResourceLimit;
        long declaredEnd = at + StatAxisValueLayout.Format4HeaderStride +
            (long)header.CombinationAxisCount * StatAxisValueLayout.Format4EntryStride;
        if (declaredEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, (ulong)at),
                out diagnostic, (ulong)_data.Length, (ulong)declaredEnd);
        status = ValidateValueNameID(header.ValueNameID, at + 4, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var seen = new HashSet<ushort>();
        int entryAt = at + StatAxisValueLayout.Format4HeaderStride;
        for (int i = 0; i < header.CombinationAxisCount; i++)
        {
            ushort axisIndex = ReadWire.U16(_data, entryAt);
            status = ValidateAxisIndex(axisIndex, designAxisCount, entryAt, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!seen.Add(axisIndex))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, (ulong)entryAt, i),
                    out diagnostic, axisIndex, 1);
            entryAt += StatAxisValueLayout.Format4EntryStride;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateAxisIndex(ushort axisIndex, int designAxisCount, int at,
        out FontDiagnostic diagnostic)
    {
        if (axisIndex >= designAxisCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, (ulong)at),
                out diagnostic, axisIndex, (ulong)designAxisCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateValueNameID(ushort valueNameID, int at,
        out FontDiagnostic diagnostic)
    {
        if (NameIDDomain.StatValueRefused(valueNameID))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, (ulong)at),
                out diagnostic, valueNameID, 256);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materializes the format-discriminated <see cref="StatAxisValue"/>
    /// (card 3.2); the format 4 combination rows are fully decoded.</summary>
    public ReadStatus TryRead(out StatAxisValue? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out StatAxisValue? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int at = StatAxisValueLayout.PrefixLength;
        switch (format)
        {
            case 1:
                status = TryReadFormat1BodyAt((ulong)at, -1, out var body1, out diagnostic);
                if (status != ReadStatus.Done) return status;
                model = new(1, body1.Flags, body1.ValueNameID, body1.AxisIndex, body1.Value,
                    0, 0, 0, 0, Array.Empty<StatCombinationEntry>());
                break;
            case 2:
                status = TryReadFormat2BodyAt((ulong)at, -1, out var body2, out diagnostic);
                if (status != ReadStatus.Done) return status;
                model = new(2, body2.Flags, body2.ValueNameID, body2.AxisIndex, 0,
                    body2.NominalValue, body2.RangeMinValue, body2.RangeMaxValue, 0,
                    Array.Empty<StatCombinationEntry>());
                break;
            case 3:
                status = TryReadFormat3BodyAt((ulong)at, -1, out var body3, out diagnostic);
                if (status != ReadStatus.Done) return status;
                model = new(3, body3.Flags, body3.ValueNameID, body3.AxisIndex, body3.Value,
                    0, 0, 0, body3.LinkedValue, Array.Empty<StatCombinationEntry>());
                break;
            default:
                status = TryReadFormat4HeaderAt((ulong)at, -1, out var header4, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var combination = new StatCombinationEntry[header4.CombinationAxisCount];
                for (int i = 0; i < header4.CombinationAxisCount; i++)
                {
                    int entryAt = at + StatAxisValueLayout.Format4HeaderStride +
                        i * StatAxisValueLayout.Format4EntryStride;
                    combination[i] = new(ReadWire.U16(_data, entryAt),
                        VariationWire.I32(_data, entryAt + 2));
                }
                model = new(4, header4.Flags, header4.ValueNameID, 0, 0, 0, 0, 0, 0, combination);
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
