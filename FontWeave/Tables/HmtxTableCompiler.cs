using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class HmtxTableCompiler
{
    public static ReadStatus TryCompile(HmtxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(HmtxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = 2 * (model.NumGlyphs + model.NumberOfHMetrics);
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

    public static ReadStatus TryWrite(HmtxTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = 2 * (model.NumGlyphs + model.NumberOfHMetrics);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(HmtxTable model, HmtxEditPlan edit,
        out HmtxTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryApply(model, edit, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryApply(HmtxTable model, HmtxEditPlan edit,
        out HmtxTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
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
            if (item.AdvanceWidth is < 0 or > ushort.MaxValue || item.LeftSideBearing is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    MetricLocation(model, item.GlyphId), out diagnostic);
            // An AW edit addresses an actual long record. Tail AW is shared storage, not
            // an independent value that can silently expand or alter the N boundary.
            if (item.AdvanceWidth.HasValue && item.GlyphId >= model.NumberOfHMetrics)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    MetricLocation(model, item.GlyphId), out diagnostic);
        }
        long bytes = 2L * (model.NumGlyphs + model.NumberOfHMetrics);
        if (!budget.Work(bytes, model.Location, out diagnostic) ||
            !budget.Buffer(bytes + 24L * edit.Metrics.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Metrics.ToArray();
        Array.Sort(requests, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    MetricLocation(model, requests[i].GlyphId), out diagnostic, (uint)requests[i].GlyphId);
        var metrics = model.LongMetrics.ToArray();
        var tail = model.TailLeftSideBearings.ToArray();
        bool changed = false;
        foreach (var item in requests)
        {
            HorizontalMetric old = model.MetricAt(item.GlyphId);
            var value = new HorizontalMetric((ushort)(item.AdvanceWidth ?? old.AdvanceWidth),
                (short)(item.LeftSideBearing ?? old.LeftSideBearing));
            changed |= old != value;
            if (item.GlyphId < metrics.Length) metrics[item.GlyphId] = value;
            else tail[item.GlyphId - metrics.Length] = value.LeftSideBearing;
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(metrics, tail, model.Location, model.HheaLocation, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryReencode(HmtxTable model, int targetN, out HmtxTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryReencode(model, targetN, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryReencode(HmtxTable model, int targetN, out HmtxTable? updated,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (targetN < 1 || targetN > model.NumGlyphs || targetN == model.NumberOfHMetrics)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)targetN), model.NumGlyphs);
        if (!budget.Work(8L * model.NumGlyphs, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort shared = model.MetricAt(targetN - 1).AdvanceWidth;
        for (int gid = targetN; gid < model.NumGlyphs; gid++)
            if (model.MetricAt(gid).AdvanceWidth != shared)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    MetricLocation(model, gid), out diagnostic, model.MetricAt(gid).AdvanceWidth, shared);
        long length = 2L * (model.NumGlyphs + targetN);
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var metrics = new HorizontalMetric[targetN];
        var tail = new short[model.NumGlyphs - targetN];
        for (int gid = 0; gid < targetN; gid++) metrics[gid] = model.MetricAt(gid);
        for (int i = 0; i < tail.Length; i++) tail[i] = model.MetricAt(targetN + i).LeftSideBearing;
        var candidate = new HmtxTable(metrics, tail, model.Location, model.HheaLocation, model.MaxpLocation);
        for (int gid = 0; gid < model.NumGlyphs; gid++)
            if (candidate.MetricAt(gid) != model.MetricAt(gid))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    MetricLocation(model, gid), out diagnostic);
        updated = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static DataLocation MetricLocation(HmtxTable model, int gid) => model.Location.At(
        DataField.Payload, gid < model.NumberOfHMetrics ? (ulong)gid * HmtxLayout.LongStride :
            (ulong)model.NumberOfHMetrics * HmtxLayout.LongStride +
            (ulong)(gid - model.NumberOfHMetrics) * HmtxLayout.TailStride, gid);

    private static void Write(HmtxTable model, Span<byte> bytes)
    {
        int at = 0;
        foreach (var metric in model.LongMetrics)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(at), metric.AdvanceWidth);
            BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(at + 2), metric.LeftSideBearing);
            at += HmtxLayout.LongStride;
        }
        foreach (short lsb in model.TailLeftSideBearings)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(at), lsb);
            at += HmtxLayout.TailStride;
        }
    }
}
