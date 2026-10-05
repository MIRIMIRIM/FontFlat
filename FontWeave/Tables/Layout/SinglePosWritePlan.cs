using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (card A6 1.1-①): source slice, wire shape, placed
// coverage target, placed device leaves with their emission target map and the
// emission work total. Emission is WritePlanned; the plan carries no payload bytes.
internal sealed record SinglePosPartPlan(int Start, int Count, ushort Format, ushort CoverageFormat,
    int RangeCount, int CoverageTarget, int Length, SinglePosTableCompiler.DeviceLeaf[] Devices,
    Dictionary<StaticDeviceTable, int> DeviceTargets, long EmissionWork);

public static partial class SinglePosTableCompiler
{
    internal sealed class DeviceLeaf(StaticDeviceTable model, int position)
    {
        internal StaticDeviceTable Model { get; } = model;
        internal int Position { get; set; } = position;
    }

    public static ReadStatus TryCompile(SinglePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(SinglePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = SinglePlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Output(plan!.Length, model.Location, out diagnostic) ||
            !budget.Work(plan.EmissionWork, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length]; WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length); output = bytes;
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(SinglePosTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = SinglePlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Fallible hooks and budgets complete before the destination capacity check (spec 7.3; A6 1.5 O4 order).
        if (!budget.Output(plan!.Length, model.Location, out diagnostic) ||
            !budget.Work(plan.EmissionWork, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination); diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryCompileSubtables(SinglePosTable model, out byte[][]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompileSubtables(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompileSubtables(SinglePosTable model, out byte[][]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        long length = 0;
        foreach (var plan in plans!) length += plan.Length;
        // The per-part arrays are this call's published output and are admitted here;
        // the plan surface no longer admits them (card A6 1.1-② deletion point).
        if (!budget.Buffer(length + 8L * plans.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[][] bytes = new byte[plans.Length][];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = new byte[plans[i].Length]; WritePlanned(model, plans[i], bytes[i]);
            budget.RecordCompiledTable(bytes[i].Length);
        }
        output = bytes; diagnostic = default; return ReadStatus.Done;
    }

    // Terminal fragment planning: the whole/single fall-back, the two-part and greedy
    // prefix candidate searches all settle here (card A6: the candidate search
    // algorithm itself is frozen; only its result becomes a reusable plan). The former
    // trailing Buffer(length + 8*plans.Count) admission of the per-part output arrays
    // is not part of the plan surface.
    internal static ReadStatus TryPlanSubtables(SinglePosTable model, out SinglePosPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        ReadStatus status = SinglePlan(model, out var whole, out diagnostic, budget);
        if (status != ReadStatus.Done && status != ReadStatus.UnsupportedFormat) return status;
        var fragments = new List<SinglePosPartPlan>();
        if (status == ReadStatus.Done) fragments.Add(whole!);
        else
        {
            if (model.Format == 1 || model.Glyphs.IsEmpty) return status;
            status = TwoParts(model, false, out var first, out var second, out diagnostic, budget);
            if (status != ReadStatus.Done && status != ReadStatus.UnsupportedFormat) return status;
            if (status != ReadStatus.Done)
            {
                status = TwoParts(model, true, out first, out second, out diagnostic, budget);
                if (status != ReadStatus.Done && status != ReadStatus.UnsupportedFormat) return status;
            }
            if (status == ReadStatus.Done) { fragments.Add(first!); fragments.Add(second!); }
            else
            {
                int start = 0;
                while (start < model.Glyphs.Length)
                {
                    // Strictly consumes at least one key. At most UInt16.MaxValue
                    // iterations, with at most 16 probes per prefix search.
                    status = Prefix(model, start, model.Glyphs.Length - start, false, out int count,
                        out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (count == 0) return OffsetFailure(model, ushort.MaxValue + 1L, out diagnostic);
                    status = MeasurePart(model, start, count, 2, true, out var part, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    fragments.Add(part!); start += count;
                }
            }
        }
        long length = 0, work = 0;
        foreach (var plan in fragments) { length += plan.Length; work += plan.EmissionWork; }
        if (!budget.Output(length, model.Location, out diagnostic) ||
            !budget.Work(work, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = fragments.ToArray();
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus SinglePlan(SinglePosTable model, out SinglePosPartPlan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = MeasurePart(model, 0, model.Glyphs.Length, model.Format, false,
            out plan, out diagnostic, budget);
        if (status != ReadStatus.UnsupportedFormat || model.Format != 2 || model.Values.IsEmpty) return status;
        FontDiagnostic nonuniformDiagnostic = diagnostic;
        if (!budget.Work(8L * model.Values.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var first = model.Values[0];
        foreach (var value in model.Values)
            if (value != first) { diagnostic = nonuniformDiagnostic; return status; }
        return MeasurePart(model, 0, model.Glyphs.Length, 1, false, out plan, out diagnostic, budget);
    }

    private static ReadStatus TwoParts(SinglePosTable model, bool suffixShort,
        out SinglePosPartPlan? first, out SinglePosPartPlan? second, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        first = second = null;
        int count = model.Glyphs.Length;
        ReadStatus status = Prefix(model, 0, count - 1, !suffixShort, out int upper, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int lo = 1, hi = count - 1, lower = count;
        for (int probe = 0; lo <= hi && probe < 16; probe++)
        {
            int mid = lo + (hi - lo) / 2;
            status = MeasurePart(model, mid, count - mid, 2, true, out var candidate, out diagnostic, budget);
            if (status != ReadStatus.Done && status != ReadStatus.UnsupportedFormat) return status;
            bool fits = status == ReadStatus.Done && (!suffixShort || candidate!.Length <= SinglePosLayout.MaximumShortPart);
            if (fits) { lower = mid; hi = mid - 1; } else lo = mid + 1;
        }
        if (lower > upper) return OffsetFailure(model, ushort.MaxValue + 1L, out diagnostic);
        int split = Math.Clamp(count / 2, lower, upper);
        status = MeasurePart(model, 0, split, 2, true, out first, out diagnostic, budget);
        return status == ReadStatus.Done
            ? MeasurePart(model, split, count - split, 2, true, out second, out diagnostic, budget) : status;
    }

    private static ReadStatus Prefix(SinglePosTable model, int start, int maximum, bool shortPart,
        out int count, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        count = 0;
        int lo = 1, hi = maximum;
        for (int probe = 0; lo <= hi && probe < 16; probe++)
        {
            int mid = lo + (hi - lo) / 2;
            ReadStatus status = MeasurePart(model, start, mid, 2, true, out var candidate, out diagnostic, budget);
            if (status != ReadStatus.Done && status != ReadStatus.UnsupportedFormat) return status;
            bool fits = status == ReadStatus.Done && (!shortPart || candidate!.Length <= SinglePosLayout.MaximumShortPart);
            if (fits) { count = mid; lo = mid + 1; } else hi = mid - 1;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus MeasurePart(SinglePosTable model, int start, int count, ushort format,
        bool canonicalCoverage, out SinglePosPartPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        int rows = format == 1 ? 1 : count;
        int header = format == 1 ? SinglePosLayout.Format1Rows : SinglePosLayout.Format2Rows;
        int bodyEnd = header + ValueStride(model.ValueFormat) * rows;
        if (!budget.Work(1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (bodyEnd > ushort.MaxValue) return OffsetFailure(model, bodyEnd, out diagnostic);
        if (!budget.Work(6L * rows + (canonicalCoverage ? count : 0), model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var keys = model.Glyphs.Slice(start, count);
        int ranges = canonicalCoverage ? CoverageTableCompiler.CountRanges(keys) : model.Coverage.RangeCount;
        ushort coverageFormat = canonicalCoverage
            ? (CoverageLayout.RangeStride * ranges < CoverageLayout.GlyphStride * count ? (ushort)2 : (ushort)1)
            : model.Coverage.Format;
        int coverageLength = CoverageLayout.HeaderLength + (coverageFormat == 1
            ? CoverageLayout.GlyphStride * count : CoverageLayout.RangeStride * ranges);
        var devices = new List<DeviceLeaf>();
        var indexes = new Dictionary<StaticDeviceTable, int>(ReferenceEqualityComparer.Instance);
        long leafBytes = coverageLength, deltaWork = 0;
        int largestLength = coverageLength, largest = -1;
        for (int i = 0; i < rows; i++)
        {
            var row = model.Values[model.Format == 1 ? 0 : start + i];
            for (int slot = 4; slot < 8; slot++)
            {
                StaticDeviceTable? device = row.DeviceAt(slot);
                if (device is null || indexes.ContainsKey(device)) continue;
                if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                int length = StaticDeviceTableCompiler.Measure(device);
                indexes.Add(device, devices.Count); devices.Add(new(device, 0)); leafBytes += length;
                deltaWork += StaticDeviceTableCompiler.EmissionWork(device) - length;
                if (length > largestLength) { largestLength = length; largest = devices.Count - 1; }
            }
        }
        long lastStart = bodyEnd + leafBytes - largestLength;
        if (lastStart > ushort.MaxValue) return OffsetFailure(model, lastStart, out diagnostic);
        long lengthTotal = bodyEnd + leafBytes;
        if (!budget.Output(lengthTotal, model.Location, out diagnostic) ||
            !budget.Buffer(8L * devices.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int cursor = bodyEnd, coverageTarget = 0;
        if (largest != -1) { coverageTarget = cursor; cursor += coverageLength; }
        for (int i = 0; i < devices.Count; i++)
        {
            if (i == largest) continue;
            devices[i].Position = cursor; cursor += StaticDeviceTableCompiler.Measure(devices[i].Model);
        }
        if (largest == -1) coverageTarget = cursor;
        else devices[largest].Position = cursor;
        // indexes is now the immutable emission target map, not a semantic reorder.
        foreach (var leaf in devices) indexes[leaf.Model] = leaf.Position;
        long emission = lengthTotal + rows + (coverageFormat == 2 ? count : 0) + deltaWork;
        plan = new(start, count, format, coverageFormat, ranges, coverageTarget, (int)lengthTotal,
            devices.ToArray(), indexes, emission);
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus OffsetFailure(SinglePosTable model, long detail, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.AtLocal(DataField.Offset, SinglePosLayout.CoverageField), out diagnostic,
            (ulong)detail, ushort.MaxValue);

    // The former private WritePart, published for direct emission into the caller's
    // final destination (Lookup payloads or a published part array); byte-for-byte
    // identical output. Coverage/device offsets are plan values written directly,
    // guarded at plan time as before (card A6 OQ-F: no new fixup helpers).
    internal static void WritePlanned(SinglePosTable model, SinglePosPartPlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteFormat(destination, plan.Format, out _, model.Location));
        CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteCoverageOffset(destination, (ushort)plan.CoverageTarget, out _, model.Location));
        CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteValueFormat(destination, model.ValueFormat, out _, model.Location));
        int rows = plan.Format == 1 ? 1 : plan.Count;
        int rowStart = plan.Format == 1 ? SinglePosLayout.Format1Rows : SinglePosLayout.Format2Rows;
        if (plan.Format == 2)
            CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteValueCountRecordAt(destination,
                SinglePosLayout.CountField, new SinglePosView.ValueCountRecordData(-1, (ushort)plan.Count), out _, model.Location));
        int stride = ValueStride(model.ValueFormat);
        for (int i = 0; i < rows; i++)
        {
            var row = model.Values[model.Format == 1 ? 0 : plan.Start + i];
            int at = rowStart + i * stride;
            for (int slot = 0; slot < 8; slot++)
            {
                if ((model.ValueFormat & (1 << slot)) == 0) continue;
                if (slot < 4)
                    CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteSignedValueAt(destination,
                        (ulong)at, new SinglePosView.SignedValueData(i, row.ScalarAt(slot)), out _, model.Location));
                else
                {
                    StaticDeviceTable? device = row.DeviceAt(slot);
                    ushort offset = device is null ? (ushort)0 : (ushort)plan.DeviceTargets[device];
                    CoverageTableCompiler.RequireWritten(SinglePosView.TryWriteDeviceOffsetAt(destination,
                        (ulong)at, new SinglePosView.DeviceOffsetData(i, offset), out _, model.Location));
                }
                at += SinglePosLayout.WordStride;
            }
        }
        CoverageTableCompiler.WritePlanned(model.Glyphs.Slice(plan.Start, plan.Count), plan.CoverageFormat,
            plan.RangeCount, destination.Slice(plan.CoverageTarget), model.Location.AtLocal(DataField.None, (ulong)plan.CoverageTarget));
        foreach (var leaf in plan.Devices) StaticDeviceTableCompiler.WritePlanned(leaf.Model, destination.Slice(leaf.Position));
    }
}
