using FontWeave.Core;

namespace FontWeave.Tables;

public static class VmtxTableCompiler
{
    public static ReadStatus TryCompile(VmtxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(VmtxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = 2 * (model.NumGlyphs + model.NumOfLongVerMetrics);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        Write(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(VmtxTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = 2 * (model.NumGlyphs + model.NumOfLongVerMetrics);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(VmtxTable model, VmtxEditPlan edit,
        out VmtxTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryApply(model, edit, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryApply(VmtxTable model, VmtxEditPlan edit,
        out VmtxTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Metrics);
        updated = null;
        if (!budget.Work(16L * edit.Metrics.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var item in edit.Metrics)
        {
            if (item.GlyphId < 0 || item.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, item.GlyphId), out diagnostic,
                    unchecked((ulong)(long)item.GlyphId), model.NumGlyphs);
            if (item.AdvanceHeight is < 0 or > ushort.MaxValue || item.TopSideBearing is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    MetricLocation(model, item.GlyphId), out diagnostic);
            // An AH edit addresses an actual long record. Tail AH is shared storage, not
            // an independent value that can silently expand or alter the N boundary.
            if (item.AdvanceHeight.HasValue && item.GlyphId >= model.NumOfLongVerMetrics)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    MetricLocation(model, item.GlyphId), out diagnostic);
        }
        long bytes = 2L * (model.NumGlyphs + model.NumOfLongVerMetrics);
        if (!budget.Work(bytes, model.Location, out diagnostic) ||
            !budget.Buffer(bytes + 24L * edit.Metrics.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Metrics.ToArray();
        Array.Sort(requests, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    MetricLocation(model, requests[i].GlyphId), out diagnostic, (uint)requests[i].GlyphId);
        var metrics = model.LongMetrics.ToArray();
        var tail = model.TailTopSideBearings.ToArray();
        bool changed = false;
        foreach (var item in requests)
        {
            VerticalMetric old = model.MetricAt(item.GlyphId);
            var value = new VerticalMetric((ushort)(item.AdvanceHeight ?? old.AdvanceHeight),
                (short)(item.TopSideBearing ?? old.TopSideBearing));
            changed |= old != value;
            if (item.GlyphId < metrics.Length) metrics[item.GlyphId] = value;
            else tail[item.GlyphId - metrics.Length] = value.TopSideBearing;
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(metrics, tail, model.Location, model.VheaLocation, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryReencode(VmtxTable model, int targetN, out VmtxTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryReencode(model, targetN, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryReencode(VmtxTable model, int targetN, out VmtxTable? updated,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (targetN < 1 || targetN > model.NumGlyphs || targetN == model.NumOfLongVerMetrics)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)targetN), model.NumGlyphs);
        if (!budget.Work(8L * model.NumGlyphs, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort shared = model.MetricAt(targetN - 1).AdvanceHeight;
        for (int gid = targetN; gid < model.NumGlyphs; gid++)
            if (model.MetricAt(gid).AdvanceHeight != shared)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    MetricLocation(model, gid), out diagnostic, model.MetricAt(gid).AdvanceHeight, shared);
        long length = 2L * (model.NumGlyphs + targetN);
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var metrics = new VerticalMetric[targetN];
        var tail = new short[model.NumGlyphs - targetN];
        for (int gid = 0; gid < targetN; gid++) metrics[gid] = model.MetricAt(gid);
        for (int i = 0; i < tail.Length; i++) tail[i] = model.MetricAt(targetN + i).TopSideBearing;
        var candidate = new VmtxTable(metrics, tail, model.Location, model.VheaLocation, model.MaxpLocation);
        for (int gid = 0; gid < model.NumGlyphs; gid++)
            if (candidate.MetricAt(gid) != model.MetricAt(gid))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    MetricLocation(model, gid), out diagnostic);
        updated = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static DataLocation MetricLocation(VmtxTable model, int gid) => model.Location.At(
        DataField.Payload, gid < model.NumOfLongVerMetrics ? (ulong)gid * VmtxLayout.LongStride :
            (ulong)model.NumOfLongVerMetrics * VmtxLayout.LongStride +
            (ulong)(gid - model.NumOfLongVerMetrics) * VmtxLayout.TailStride, gid);

    private static void Write(VmtxTable model, Span<byte> bytes)
    {
        // After the capacity preflight these generated writers cannot fail; a failure
        // is a writer contract bug and surfaces loudly.
        for (int i = 0; i < model.LongMetrics.Length; i++)
        {
            var metric = model.LongMetrics[i];
            RequireWritten(VmtxView.TryWriteLongMetricAt(bytes, (ulong)i * VmtxLayout.LongStride,
                new VmtxView.LongMetricData(i, metric.AdvanceHeight, metric.TopSideBearing),
                out _, model.Location));
        }
        for (int i = 0; i < model.TailTopSideBearings.Length; i++)
        {
            RequireWritten(VmtxView.TryWriteTailBearingAt(bytes,
                (ulong)model.NumOfLongVerMetrics * VmtxLayout.LongStride + (ulong)i * VmtxLayout.TailStride,
                new VmtxView.TailBearingData(i, model.TailTopSideBearings[i]), out _, model.Location));
        }
    }

    // Same contract as the vorg emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted vmtx write failed.");
    }
}
