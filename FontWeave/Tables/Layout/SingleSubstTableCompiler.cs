using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (card A6 1.1-①): source slice, wire shape, coverage
// shape and the plan-time coverage fixup value. Emission is WritePlanned; the plan
// carries no payload bytes, so the per-part output arrays of the former inline
// encoding no longer exist on the plan surface.
internal sealed record SingleSubstPartPlan(int Start, int Count, ushort Format, ushort CoverageFormat,
    int CoverageRangeCount, int Length, ushort CoverageOffset);

public static class SingleSubstTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort> substitutes,
        int numGlyphs, ushort format, out SingleSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1) =>
        TryCreate(glyphs, substitutes, numGlyphs, format, out model, out diagnostic,
            new BuildBudget(limits), location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort> substitutes,
        int numGlyphs, ushort format, out SingleSubstTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (glyphs.Length != substitutes.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, SingleSubstLayout.BodyOffset),
                out diagnostic, (ulong)substitutes.Length, (ulong)glyphs.Length);
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, SingleSubstLayout.BodyOffset),
                out diagnostic, (ulong)glyphs.Length, ushort.MaxValue);
        // Replacement validation/copy, plus the requested format-1 uniform-delta check.
        if (!budget.Work((format == 1 ? 3L : 2L) * substitutes.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < substitutes.Length; i++)
            if (substitutes[i] >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId, SingleSubstLayout.HeaderLength +
                        (ulong)i * SingleSubstLayout.SubstituteStride, i),
                    out diagnostic, substitutes[i], (ulong)numGlyphs);
        if (format == 1 && !HasUniformDelta(glyphs, substitutes, out _))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, SingleSubstLayout.BodyOffset), out diagnostic, format, 2);
        int coverageAt = SingleSubstLayout.HeaderLength + (format == 2 ? SingleSubstLayout.SubstituteStride * glyphs.Length : 0);
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location.AtLocal(DataField.None, (ulong)coverageAt));
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * substitutes.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(coverage!, substitutes.ToArray(), format, location, coverageAt + CoverageTableCompiler.Measure(coverage!));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Compiles one subtable. Use TryCompileSubtables for internally oversized format-2 rows.</summary>
    public static ReadStatus TryCompile(SingleSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(SingleSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (!budget.Work(model.Glyphs.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = PlanSingle(model, out var plan, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(EmissionWork(plan), model.Location, out diagnostic) ||
            !budget.Output(plan.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(SingleSubstTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        if (!budget.Work(model.Glyphs.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = PlanSingle(model, out var plan, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        if (!budget.Work(EmissionWork(plan), model.Location, out diagnostic) ||
            !budget.Output(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic, key-disjoint subtable splitting without Extension wrappers.</summary>
    public static ReadStatus TryCompileSubtables(SingleSubstTable model, out byte[][]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompileSubtables(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompileSubtables(SingleSubstTable model, out byte[][]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int total = 0;
        foreach (var plan in plans!) total += plan.Length;
        // The per-part arrays are this call's published output and are admitted here;
        // the plan surface no longer admits them (card A6 1.1-② deletion point).
        if (!budget.Buffer(total + 8L * plans.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[plans.Length][];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = new byte[plans[i].Length];
            WritePlanned(model, plans[i], bytes[i]);
            budget.RecordCompiledTable(bytes[i].Length);
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Terminal fragment planning: split decision, binary prefix search and per-part
    // coverage fixups all settle here. The former trailing Buffer(total + 8*partCount)
    // admission of the per-part output arrays is not part of the plan surface.
    internal static ReadStatus TryPlanSubtables(SingleSubstTable model, out SingleSubstPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int count = model.Glyphs.Length;
        if (!budget.Work(32L * count + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int partCount = model.Format == 1 || count <= SingleSubstLayout.MaximumFormat2Rows ||
            HasUniformDelta(model.Glyphs, model.Substitutes, out _) ? 1 :
            (count + SingleSubstLayout.MaximumFormat2Rows - 1) / SingleSubstLayout.MaximumFormat2Rows;
        if (!budget.Buffer(20L * partCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var fragments = new SingleSubstPartPlan[partCount];
        ReadStatus status;
        if (partCount == 1)
        {
            status = PlanSingle(model, out fragments[0], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else if (partCount == 2)
        {
            int minimumFirst = count - SingleSubstLayout.MaximumFormat2Rows;
            int lo = minimumFirst, hi = count / 2, first = minimumFirst;
            // Prefix wire length is monotone. At most 16 probes for the UInt16 GID domain.
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                status = PlanSplit(model, 0, mid, out var candidate, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (candidate!.Length <= SingleSubstLayout.MaximumFirstSplitLength)
                {
                    first = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            status = PlanSplit(model, 0, first, out fragments[0], out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = PlanSplit(model, first, count - first, out fragments[1], out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (fragments[0].Length > SingleSubstLayout.MaximumFirstSplitLength)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField),
                    out diagnostic, (ulong)fragments[0].Length, SingleSubstLayout.MaximumFirstSplitLength);
        }
        else
        {
            int at = 0;
            for (int i = 0; i < partCount; i++)
            {
                int rows = (count - at) / (partCount - i);
                status = PlanSplit(model, at, rows, out fragments[i], out diagnostic);
                if (status != ReadStatus.Done) return status;
                at += rows;
            }
        }
        int total = 0;
        long emissionWork = 0;
        foreach (var plan in fragments)
        {
            total += plan.Length;
            emissionWork += EmissionWork(plan);
        }
        if (!budget.Work(emissionWork, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = fragments;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus PlanSingle(SingleSubstTable model, out SingleSubstPartPlan plan,
        out FontDiagnostic diagnostic)
    {
        // The former readonly-record-struct default: no plan exists on refusal.
        plan = null!;
        ushort format = model.Format;
        if (format == 2 && model.Glyphs.Length > SingleSubstLayout.MaximumFormat2Rows)
        {
            if (!HasUniformDelta(model.Glyphs, model.Substitutes, out _))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField),
                    out diagnostic, (ulong)(SingleSubstLayout.HeaderLength +
                        SingleSubstLayout.SubstituteStride * model.Glyphs.Length), ushort.MaxValue);
            format = 1;
        }
        int bodyEnd = SingleSubstLayout.HeaderLength + (format == 2 ? SingleSubstLayout.SubstituteStride * model.Glyphs.Length : 0);
        ReadStatus status = SingleSubstView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)(SingleSubstLayout.HeaderLength +
                (format == 2 ? SingleSubstLayout.SubstituteStride * model.Glyphs.Length : 0)),
            out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField));
        if (status != ReadStatus.Done) return status;
        plan = new(0, model.Glyphs.Length, format, model.Coverage.Format, model.Coverage.RangeCount,
            bodyEnd + CoverageTableCompiler.Measure(model.Coverage), coverageOffset);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus PlanSplit(SingleSubstTable model, int start, int count, out SingleSubstPartPlan plan,
        out FontDiagnostic diagnostic)
    {
        plan = null!;
        ReadOnlySpan<ushort> glyphs = model.Glyphs.Slice(start, count);
        int rangeCount = CoverageTableCompiler.CountRanges(glyphs);
        ushort coverageFormat = CoverageLayout.RangeStride * rangeCount < CoverageLayout.GlyphStride * count
            ? (ushort)2 : (ushort)1;
        int bodyEnd = SingleSubstLayout.HeaderLength + SingleSubstLayout.SubstituteStride * count;
        int coverageLength = CoverageLayout.HeaderLength + (coverageFormat == 1
            ? CoverageLayout.GlyphStride * count : CoverageLayout.RangeStride * rangeCount);
        // The split coverage position is a plan invariant within the UInt16 field, so the
        // generated fixup helper cannot fail here; refusing it formally retires the
        // writer-side silent (ushort) narrowing.
        ReadStatus status = SingleSubstView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)(SingleSubstLayout.HeaderLength + SingleSubstLayout.SubstituteStride * count),
            out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField));
        if (status != ReadStatus.Done) return status;
        plan = new(start, count, 2, coverageFormat, rangeCount, bodyEnd + coverageLength, coverageOffset);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The former private WritePart, published for direct emission into the caller's
    // final destination (Lookup payloads or a published part array); byte-for-byte
    // identical output.
    internal static void WritePlanned(SingleSubstTable model, SingleSubstPartPlan plan, Span<byte> destination)
    {
        ReadOnlySpan<ushort> glyphs = model.Glyphs.Slice(plan.Start, plan.Count);
        ReadOnlySpan<ushort> substitutes = model.Substitutes.Slice(plan.Start, plan.Count);
        int coverageAt = SingleSubstLayout.HeaderLength + (plan.Format == 2 ? SingleSubstLayout.SubstituteStride * plan.Count : 0);
        CoverageTableCompiler.RequireWritten(SingleSubstView.TryWriteFormat(destination, plan.Format, out _, model.Location));
        // The coverage offset comes from the plan's fixup-encoded value; the emission path
        // no longer narrows anything silently.
        CoverageTableCompiler.RequireWritten(SingleSubstView.TryWriteCoverageOffset(destination,
            plan.CoverageOffset, out _, model.Location));
        if (plan.Format == 1)
        {
            // The model or PlanSingle has already established the uniform-delta invariant.
            short delta = glyphs.IsEmpty ? (short)0 : unchecked((short)(substitutes[0] - glyphs[0]));
            CoverageTableCompiler.RequireWritten(SingleSubstView.TryWriteDeltaRecordAt(destination,
                SingleSubstLayout.BodyOffset, new SingleSubstView.DeltaRecordData(-1, delta), out _, model.Location));
        }
        else
        {
            CoverageTableCompiler.RequireWritten(SingleSubstView.TryWriteSubstituteHeaderAt(destination,
                SingleSubstLayout.BodyOffset, new SingleSubstView.SubstituteHeaderData(-1, (ushort)plan.Count), out _, model.Location));
            for (int i = 0; i < substitutes.Length; i++)
                CoverageTableCompiler.RequireWritten(SingleSubstView.TryWriteSubstituteRecordAt(destination,
                    SingleSubstLayout.HeaderLength + (ulong)i * SingleSubstLayout.SubstituteStride,
                    new SingleSubstView.SubstituteRecordData(i, substitutes[i]), out _, model.Location));
        }
        CoverageTableCompiler.WritePlanned(glyphs, plan.CoverageFormat, plan.CoverageRangeCount, destination.Slice(coverageAt),
            model.Location.AtLocal(DataField.None, (ulong)coverageAt));
    }

    private static long EmissionWork(SingleSubstPartPlan plan) => plan.Length +
        (plan.CoverageFormat == 2 ? (long)plan.Count : 0);

    private static bool HasUniformDelta(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ushort> substitutes, out short delta)
    {
        delta = glyphs.IsEmpty ? (short)0 : unchecked((short)(substitutes[0] - glyphs[0]));
        for (int i = 1; i < glyphs.Length; i++)
            if (unchecked((ushort)(glyphs[i] + delta)) != substitutes[i]) return false;
        return true;
    }
}
