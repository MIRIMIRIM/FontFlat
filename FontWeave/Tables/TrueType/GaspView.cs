using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Semantic half of the single gasp View.
///
/// The generated half owns storage, binding, the version gate and per-element slicing. This
/// half adds the whole-table rules the flat schema cannot express: the exact length relation,
/// strict ppem ordering with a final sentinel, a non-zero range count, the declared range
/// budget, and the version-dependent behaviour mask.
///
/// The two version masks differ, which is the format boundary this slice exists to enforce:
/// version 0 defines only grid fitting and grayscale, while version 1 adds the two symmetric
/// bits. fontTools does not necessarily validate these masks, so the rules are enforced here
/// and proven by independent fixtures rather than delegated to an external tool.
/// </summary>
public readonly ref partial struct GaspView
{
    /// <summary>Validates the whole table without producing a model.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryStructure(out _, out _, out diagnostic);
    }

    /// <summary>Reads the version, the range count and every range into an owned model.
    /// A failure publishes no model, so validation is atomic.</summary>
    public ReadStatus TryRead(out GaspTable? table, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        table = null;
        var budget = new BuildBudget(limits);
        // The input budget is charged against the table as it is read, matching the other
        // table views, so an over-budget source is refused before any work is done.
        if (!budget.Input(RawBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out table, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out GaspTable? table, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        table = null;
        if (!budget.Work(RawBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = TryRequireKnownVersion(out ulong rawVersion, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryStructure(out int count, out var ranges, out diagnostic);
        if (status != ReadStatus.Done) return status;
        table = new GaspTable((uint)rawVersion, ranges!, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The whole-table structural rules, shared by validation and reading.</summary>
    private ReadStatus TryStructure(out int count, out GaspRange[]? ranges,
        out FontDiagnostic diagnostic)
    {
        count = 0;
        ranges = null;
        ReadStatus status = TryGetNumRanges(out ushort declared, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (declared == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, GaspLayout.NumRangesAt), out diagnostic, 0, 1);
        if (declared > GaspLayout.MaxRanges)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Count, GaspLayout.NumRangesAt), out diagnostic,
                declared, GaspLayout.MaxRanges);
        // The table length is exact: the header plus four bytes per declared range. A short
        // table is truncation; a long one has trailing bytes that are not part of any range.
        int expected = GaspLayout.HeaderLength + declared * GaspLayout.RangeStride;
        if (RawBytes.Length < expected)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, (ulong)RawBytes.Length), out diagnostic,
                (ulong)RawBytes.Length, (ulong)expected);
        if (RawBytes.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat,
                DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Payload, (ulong)expected), out diagnostic,
                (ulong)RawBytes.Length, (ulong)expected);
        uint version = Version;
        ushort mask = version == GaspLayout.Version0
            ? GaspLayout.Version0Mask : GaspLayout.Version1Mask;
        var list = new GaspRange[declared];
        ushort previous = 0;
        for (int ordinal = 0; ordinal < declared; ordinal++)
        {
            status = TryGetRange(ordinal, out RangeData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // rangeMaxPPEM is strictly increasing; an equal or smaller value is refused with
            // the offending ordinal, because the ordering is what makes the ranges meaningful.
            if (ordinal > 0 && record.RangeMaxPpem <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (ulong)(GaspLayout.HeaderLength +
                        ordinal * GaspLayout.RangeStride), ordinal), out diagnostic,
                    record.RangeMaxPpem, (ulong)previous + 1);
            previous = record.RangeMaxPpem;
            // The behaviour bits must lie inside this version's mask. A version 0 table that
            // sets a version 1 bit is refused rather than reinterpreted.
            if ((record.RangeGaspBehavior & ~mask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Flags, (ulong)(GaspLayout.HeaderLength +
                        ordinal * GaspLayout.RangeStride + GaspLayout.RangeBehaviorFieldAt),
                        ordinal), out diagnostic, record.RangeGaspBehavior, mask);
            list[ordinal] = new(ordinal, record.RangeMaxPpem, record.RangeGaspBehavior);
        }
        // The final range is a sentinel for the largest size; without it the table has no
        // defined behaviour above the last threshold.
        if (list[^1].RangeMaxPpem != GaspLayout.SentinelPpem)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (ulong)(GaspLayout.HeaderLength +
                    (declared - 1) * GaspLayout.RangeStride), declared - 1), out diagnostic,
                list[^1].RangeMaxPpem, GaspLayout.SentinelPpem);
        count = declared;
        ranges = list;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads one range by ordinal.
    ///
    /// A compatibility projection over the generated <c>TryGetRange(int, out RangeData, …)</c>
    /// accessor, not a second reader: the version gate deliberately runs here, ahead of the
    /// generated ordinal gate, so an unknown version with a plausible ordinal reports
    /// UnsupportedVersion rather than an index failure.</summary>
    public ReadStatus TryGetRange(int ordinal, out GaspRange range, out FontDiagnostic diagnostic)
    {
        range = default;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetRange(ordinal, out RangeData record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        range = new(ordinal, record.RangeMaxPpem, record.RangeGaspBehavior);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
