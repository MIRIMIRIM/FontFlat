using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One gasp range replacement. The ordinal addresses the existing range sequence.</summary>
public sealed record GaspRangeEdit
{
    public required int Ordinal { get; init; }
    public ushort? RangeMaxPpem { get; init; }
    public ushort? RangeGaspBehavior { get; init; }
}

/// <summary>A gasp rewrite: version change, range replacements, removals and appends.</summary>
public sealed record GaspEditPlan
{
    /// <summary>Replacement version, or null to keep the current one.</summary>
    public uint? Version { get; init; }

    public IReadOnlyList<GaspRangeEdit> Ranges { get; init; } = [];

    /// <summary>Range ordinals to drop, applied against the original sequence.</summary>
    public IReadOnlyList<int> Remove { get; init; } = [];

    /// <summary>Ranges appended after the existing ones.</summary>
    public IReadOnlyList<GaspRange> Append { get; init; } = [];
}

/// <summary>Canonical gasp encoding.
///
/// The form is minimal: the four-byte header followed by four bytes per range, with no
/// padding. Every plan is validated against the resulting version's behaviour mask and the
/// ordering rules before anything is written, so an invalid plan is refused rather than
/// encoded. The final 0xFFFF sentinel is required, and a plan that would leave another range
/// after the sentinel is refused because the sentinel would no longer be last.</summary>
public static class GaspTableCompiler
{
    public static ReadStatus TryCompile(GaspTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        return TryCompile(model, out output, out diagnostic, budget);
    }

    private static ReadStatus TryCompile(GaspTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = RequireStructure(model, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int length = model.EncodedLength;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        // After the budget preflight these generated writers cannot fail; a failure is
        // a writer contract bug and surfaces loudly.
        RequireWritten(GaspView.TryWriteVersion(bytes, (ushort)model.Version, out _, model.Location));
        RequireWritten(GaspView.TryWriteNumRanges(bytes, (ushort)model.RangeCount, out _, model.Location));
        for (int i = 0; i < model.RangeCount; i++)
        {
            ulong at = (ulong)(GaspLayout.HeaderLength + i * GaspLayout.RangeStride);
            RequireWritten(GaspView.TryWriteRangeAt(bytes, at,
                new GaspView.RangeData(i, model[i].RangeMaxPpem, model[i].RangeGaspBehavior),
                out _, model.Location));
        }
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The whole-table rules a model must satisfy before it can be encoded.</summary>
    private static ReadStatus RequireStructure(GaspTable model, out FontDiagnostic diagnostic)
    {
        if (model.Version is not (GaspLayout.Version0 or GaspLayout.Version1))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                model.Location.At(DataField.Version, GaspLayout.VersionAt), out diagnostic,
                model.Version, GaspLayout.Version1);
        if (model.RangeCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Count, GaspLayout.NumRangesAt), out diagnostic, 0, 1);
        if (model.RangeCount > GaspLayout.MaxRanges)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.Count, GaspLayout.NumRangesAt), out diagnostic,
                (ulong)model.RangeCount, GaspLayout.MaxRanges);
        ushort mask = model.LegalMask;
        ushort previous = 0;
        for (int i = 0; i < model.RangeCount; i++)
        {
            var range = model[i];
            if (i > 0 && range.RangeMaxPpem <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Count,
                        (ulong)(GaspLayout.HeaderLength + i * GaspLayout.RangeStride), i),
                    out diagnostic, range.RangeMaxPpem, (ulong)previous + 1);
            previous = range.RangeMaxPpem;
            if ((range.RangeGaspBehavior & ~mask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Flags,
                        (ulong)(GaspLayout.HeaderLength + i * GaspLayout.RangeStride +
                            GaspLayout.RangeBehaviorFieldAt), i), out diagnostic,
                    range.RangeGaspBehavior, mask);
        }
        if (model[model.RangeCount - 1].RangeMaxPpem != GaspLayout.SentinelPpem)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Count,
                    (ulong)(GaspLayout.HeaderLength +
                        (model.RangeCount - 1) * GaspLayout.RangeStride), model.RangeCount - 1),
                out diagnostic, model[model.RangeCount - 1].RangeMaxPpem,
                GaspLayout.SentinelPpem);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Applies a plan and returns the rebuilt bytes. The resulting table is validated
    /// as a whole, so a plan that would produce an unordered sequence, a wrong sentinel or a
    /// flag outside the target version's mask is refused with nothing written.</summary>
    public static ReadStatus TryApply(GaspTable model, GaspEditPlan plan, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        output = null;
        uint version = plan.Version ?? model.Version;
        if (version is not (GaspLayout.Version0 or GaspLayout.Version1))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                model.Location.At(DataField.Version, GaspLayout.VersionAt), out diagnostic,
                version, GaspLayout.Version1);
        var removals = new HashSet<int>();
        foreach (int ordinal in plan.Remove)
        {
            if (ordinal < 0 || ordinal >= model.RangeCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, GaspLayout.NumRangesAt, ordinal),
                    out diagnostic, unchecked((ulong)(long)ordinal), (ulong)model.RangeCount);
            removals.Add(ordinal);
        }
        var edits = new Dictionary<int, GaspRangeEdit>();
        foreach (var edit in plan.Ranges)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.Ordinal < 0 || edit.Ordinal >= model.RangeCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, GaspLayout.NumRangesAt, edit.Ordinal),
                    out diagnostic, unchecked((ulong)(long)edit.Ordinal), (ulong)model.RangeCount);
            if (removals.Contains(edit.Ordinal))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Count, GaspLayout.NumRangesAt, edit.Ordinal),
                    out diagnostic, unchecked((ulong)(long)edit.Ordinal), 0);
            edits[edit.Ordinal] = edit;
        }
        var kept = new List<GaspRange>();
        for (int ordinal = 0; ordinal < model.RangeCount; ordinal++)
        {
            if (removals.Contains(ordinal)) continue;
            var current = model[ordinal];
            if (edits.TryGetValue(ordinal, out var edit))
                current = new(kept.Count, edit.RangeMaxPpem ?? current.RangeMaxPpem,
                    edit.RangeGaspBehavior ?? current.RangeGaspBehavior);
            else current = current with { Ordinal = kept.Count };
            kept.Add(current);
        }
        kept.AddRange(plan.Append.Select(range => range with { Ordinal = kept.Count }));
        if (kept.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Count, GaspLayout.NumRangesAt), out diagnostic, 0, 1);
        var edited = new GaspTable(version, [.. kept], model.Location);
        return TryCompile(edited, out output, out diagnostic, limits);
    }

    /// <summary>Writes a canonical encoding into a caller-supplied destination, writing
    /// nothing unless the whole table fits.</summary>
    public static ReadStatus TryWrite(GaspTable model, Span<byte> destination, out int written,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        written = 0;
        ReadStatus status = TryCompile(model, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)bytes.Length);
        bytes.CopyTo(destination);
        written = bytes.Length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds a model from an explicit range sequence.</summary>
    public static GaspTable FromRanges(uint version, ReadOnlySpan<GaspRange> ranges,
        DataLocation location = default)
    {
        var copy = ranges.ToArray();
        for (int i = 0; i < copy.Length; i++) copy[i] = copy[i] with { Ordinal = i };
        return new(version, copy,
            location == default ? DataLocation.Anonymous(GaspLayout.Tag) : location);
    }

    // Same contract as the pclt emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted gasp write failed.");
    }
}
