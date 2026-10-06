using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (card A6 1.1-① shape): the wire layout facts and the
// plan-time fixup value. One subtable is one part; the T4-01 internal split stays
// type-1-specific (OQ-T4-03-6), so an Offset16-unreachable layout is a finite refusal
// and multi-subtable models remain caller-driven key-disjoint sharding.
internal sealed record MultipleSubstPartPlan(int Length, ushort CoverageFormat,
    int CoverageRangeCount, ushort CoverageOffset);

public static class MultipleSubstTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> sequences,
        int numGlyphs, out MultipleSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1) =>
        TryCreate(glyphs, sequences, numGlyphs, out model, out diagnostic, new BuildBudget(limits),
            location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> sequences,
        int numGlyphs, out MultipleSubstTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length != sequences.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MultipleSubstLayout.CountAt),
                out diagnostic, (ulong)sequences.Length, (ulong)glyphs.Length);
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MultipleSubstLayout.CountAt),
                out diagnostic, (ulong)glyphs.Length, ushort.MaxValue);
        long rows = 0;
        for (int i = 0; i < sequences.Length; i++) rows += sequences[i].Length;
        if (!budget.Work(glyphs.Length + rows + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The empty sequence is the legal deletion no-op (OQ-T4-03-2, W9); every
        // substitute stays inside the glyph domain.
        long rowAt = MultipleSubstLayout.MinimumHeader + MultipleSubstLayout.OffsetStride * (long)glyphs.Length;
        for (int i = 0; i < sequences.Length; i++)
        {
            ReadOnlySpan<ushort> sequence = sequences[i];
            for (int j = 0; j < sequence.Length; j++)
                if (sequence[j] >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.GlyphId, (ulong)rowAt + 2UL + (ulong)j * 2UL, i),
                        out diagnostic, sequence[j], (ulong)numGlyphs);
            rowAt += 2 + 2L * sequence.Length;
        }
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(rows * 2L + 8L * sequences.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new ushort[sequences.Length][];
        long consumed = MultipleSubstLayout.MinimumHeader + MultipleSubstLayout.OffsetStride * (long)sequences.Length;
        for (int i = 0; i < sequences.Length; i++)
        {
            copy[i] = sequences[i].ToArray();
            consumed += 2 + 2L * copy[i].Length;
        }
        consumed += CoverageTableCompiler.Measure(coverage!);
        model = new(coverage!, copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(MultipleSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MultipleSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int total = plans![0].Length;
        if (!budget.Buffer(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[total];
        WritePlanned(model, plans[0], bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MultipleSubstTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Fallible hooks and budgets complete before the destination capacity check
        // (spec 7.3; A6 1.5 O4 order); the destination is untouched on refusal.
        if (destination.Length < plans![0].Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plans[0].Length);
        WritePlanned(model, plans[0], destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Terminal fragment planning: one part per subtable, deterministic layout (header,
    // offset records, sequence bodies in order, coverage last) with every fixup through
    // the generated per-reference helpers, whose too-narrow refusal IS the registered
    // finite overflow refusal (UnsupportedFormat/UnsupportedOperation, Detail=target,
    // Expected=65535, OQ-T4-03-6: no automatic split).
    internal static ReadStatus TryPlanSubtables(MultipleSubstTable model, out MultipleSubstPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int count = model.SequenceCount;
        if (!budget.Work(32L * count + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        long bodies = 0;
        for (int i = 0; i < count; i++) bodies += 2L + 2L * model.GetSequence(i).Length;
        int coverageAt = MultipleSubstLayout.MinimumHeader +
            MultipleSubstLayout.OffsetStride * count + (int)bodies;
        ReadStatus status = MultipleSubstView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, MultipleSubstLayout.CoverageOffsetField));
        if (status != ReadStatus.Done) return status;
        long cursor = MultipleSubstLayout.MinimumHeader + MultipleSubstLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            status = MultipleSubstView.TryGetSequenceOffsetValue(present: true, origin: 0UL,
                target: (ulong)cursor, out _, out diagnostic,
                model.Location.AtLocal(DataField.Offset,
                    MultipleSubstLayout.MinimumHeader + (ulong)i * MultipleSubstLayout.OffsetStride, i));
            if (status != ReadStatus.Done) return status;
            cursor += 2 + 2L * model.GetSequence(i).Length;
        }
        var plan = new MultipleSubstPartPlan(coverageAt + CoverageTableCompiler.Measure(model.Coverage),
            model.Coverage.Format, model.Coverage.RangeCount, coverageOffset);
        if (!budget.Work(EmissionWork(plan), model.Location, out diagnostic) ||
            !budget.Output(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [plan];
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The former private WritePart, published for direct emission into a Lookup slot or
    // an extension body; byte-for-byte deterministic from the model and plan.
    internal static void WritePlanned(MultipleSubstTable model, MultipleSubstPartPlan plan, Span<byte> destination)
    {
        int count = model.SequenceCount;
        CoverageTableCompiler.RequireWritten(MultipleSubstView.TryWriteSubstFormat(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(MultipleSubstView.TryWriteCoverageOffset(destination,
            plan.CoverageOffset, out _, model.Location));
        CoverageTableCompiler.RequireWritten(MultipleSubstView.TryWriteSequenceCount(destination,
            (ushort)count, out _, model.Location));
        long cursor = MultipleSubstLayout.MinimumHeader + MultipleSubstLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            CoverageTableCompiler.RequireWritten(MultipleSubstView.TryWriteSequenceOffsetRecordAt(destination,
                MultipleSubstLayout.MinimumHeader + (ulong)i * MultipleSubstLayout.OffsetStride,
                new MultipleSubstView.SequenceOffsetRecordData(i, (ushort)cursor), out _, model.Location));
            ReadOnlySpan<ushort> sequence = model.GetSequence(i);
            CoverageTableCompiler.RequireWritten(SequenceView.TryWriteGlyphCount(
                destination.Slice((int)cursor), (ushort)sequence.Length, out _, model.Location));
            for (int j = 0; j < sequence.Length; j++)
                CoverageTableCompiler.RequireWritten(SequenceView.TryWriteSubstituteRecordAt(
                    destination.Slice((int)cursor),
                    SequenceLayout.HeaderLength + (ulong)j * SequenceLayout.SubstituteStride,
                    new SequenceView.SubstituteRecordData(j, sequence[j]), out _, model.Location));
            cursor += 2 + 2L * sequence.Length;
        }
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageOffset), model.Location);
    }

    private static long EmissionWork(MultipleSubstPartPlan plan) => plan.Length +
        (plan.CoverageFormat == 2 ? (long)plan.CoverageRangeCount * 2 : 0);
}
