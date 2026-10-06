using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The AlternateSubst twin of the MultipleSubst compiler: one subtable per part,
// deterministic layout (header, set offsets, set bodies in order, coverage last), the
// generated fixup helpers own the finite overflow refusal (OQ-T4-03-6: no split).
internal sealed record AlternateSubstPartPlan(int Length, ushort CoverageFormat,
    int CoverageRangeCount, ushort CoverageOffset);

public static class AlternateSubstTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> alternateSets,
        int numGlyphs, out AlternateSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1) =>
        TryCreate(glyphs, alternateSets, numGlyphs, out model, out diagnostic, new BuildBudget(limits),
            location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort[]> alternateSets,
        int numGlyphs, out AlternateSubstTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length != alternateSets.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, AlternateSubstLayout.CountAt),
                out diagnostic, (ulong)alternateSets.Length, (ulong)glyphs.Length);
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, AlternateSubstLayout.CountAt),
                out diagnostic, (ulong)glyphs.Length, ushort.MaxValue);
        long rows = 0;
        for (int i = 0; i < alternateSets.Length; i++) rows += alternateSets[i].Length;
        if (!budget.Work(glyphs.Length + rows + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Alternates are unordered by spec; the empty set is the legal no-op
        // (OQ-T4-03-2) and every alternate stays inside the glyph domain.
        long rowAt = AlternateSubstLayout.MinimumHeader + AlternateSubstLayout.OffsetStride * (long)glyphs.Length;
        for (int i = 0; i < alternateSets.Length; i++)
        {
            ReadOnlySpan<ushort> set = alternateSets[i];
            for (int j = 0; j < set.Length; j++)
                if (set[j] >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.GlyphId, (ulong)rowAt + 2UL + (ulong)j * 2UL, i),
                        out diagnostic, set[j], (ulong)numGlyphs);
            rowAt += 2 + 2L * set.Length;
        }
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(rows * 2L + 8L * alternateSets.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new ushort[alternateSets.Length][];
        long consumed = AlternateSubstLayout.MinimumHeader + AlternateSubstLayout.OffsetStride * (long)alternateSets.Length;
        for (int i = 0; i < alternateSets.Length; i++)
        {
            copy[i] = alternateSets[i].ToArray();
            consumed += 2 + 2L * copy[i].Length;
        }
        consumed += CoverageTableCompiler.Measure(coverage!);
        model = new(coverage!, copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(AlternateSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(AlternateSubstTable model, out byte[]? output,
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

    public static ReadStatus TryWrite(AlternateSubstTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plans![0].Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plans[0].Length);
        WritePlanned(model, plans[0], destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(AlternateSubstTable model, out AlternateSubstPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int count = model.AlternateSetCount;
        if (!budget.Work(32L * count + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        long bodies = 0;
        for (int i = 0; i < count; i++) bodies += 2L + 2L * model.GetAlternateSet(i).Length;
        int coverageAt = AlternateSubstLayout.MinimumHeader +
            AlternateSubstLayout.OffsetStride * count + (int)bodies;
        ReadStatus status = AlternateSubstView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, AlternateSubstLayout.CoverageOffsetField));
        if (status != ReadStatus.Done) return status;
        long cursor = AlternateSubstLayout.MinimumHeader + AlternateSubstLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            status = AlternateSubstView.TryGetAlternateSetOffsetValue(present: true, origin: 0UL,
                target: (ulong)cursor, out _, out diagnostic,
                model.Location.AtLocal(DataField.Offset,
                    AlternateSubstLayout.MinimumHeader + (ulong)i * AlternateSubstLayout.OffsetStride, i));
            if (status != ReadStatus.Done) return status;
            cursor += 2 + 2L * model.GetAlternateSet(i).Length;
        }
        var plan = new AlternateSubstPartPlan(coverageAt + CoverageTableCompiler.Measure(model.Coverage),
            model.Coverage.Format, model.Coverage.RangeCount, coverageOffset);
        if (!budget.Work(EmissionWork(plan), model.Location, out diagnostic) ||
            !budget.Output(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [plan];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void WritePlanned(AlternateSubstTable model, AlternateSubstPartPlan plan, Span<byte> destination)
    {
        int count = model.AlternateSetCount;
        CoverageTableCompiler.RequireWritten(AlternateSubstView.TryWriteSubstFormat(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(AlternateSubstView.TryWriteCoverageOffset(destination,
            plan.CoverageOffset, out _, model.Location));
        CoverageTableCompiler.RequireWritten(AlternateSubstView.TryWriteAlternateSetCount(destination,
            (ushort)count, out _, model.Location));
        long cursor = AlternateSubstLayout.MinimumHeader + AlternateSubstLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            CoverageTableCompiler.RequireWritten(AlternateSubstView.TryWriteAlternateSetOffsetRecordAt(destination,
                AlternateSubstLayout.MinimumHeader + (ulong)i * AlternateSubstLayout.OffsetStride,
                new AlternateSubstView.AlternateSetOffsetRecordData(i, (ushort)cursor), out _, model.Location));
            ReadOnlySpan<ushort> set = model.GetAlternateSet(i);
            CoverageTableCompiler.RequireWritten(AlternateSetView.TryWriteGlyphCount(
                destination.Slice((int)cursor), (ushort)set.Length, out _, model.Location));
            for (int j = 0; j < set.Length; j++)
                CoverageTableCompiler.RequireWritten(AlternateSetView.TryWriteAlternateRecordAt(
                    destination.Slice((int)cursor),
                    AlternateSetLayout.HeaderLength + (ulong)j * AlternateSetLayout.AlternateStride,
                    new AlternateSetView.AlternateRecordData(j, set[j]), out _, model.Location));
            cursor += 2 + 2L * set.Length;
        }
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageOffset), model.Location);
    }

    private static long EmissionWork(AlternateSubstPartPlan plan) => plan.Length +
        (plan.CoverageFormat == 2 ? (long)plan.CoverageRangeCount * 2 : 0);
}
