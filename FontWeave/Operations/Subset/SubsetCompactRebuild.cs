using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap rebuild stages over the frozen map (SC card §2, commit ③:
/// the identity spine and the metrics families).
///
/// glyf is the byte-faithful ordered splice with in-place component-index patching, loca
/// carries the output-domain offsets in the source format, cmap transforms the pruned
/// mapping values through the map before the canonical pair build, maxp changes only
/// numGlyphs (OQ-SC-1), and the hhea/hmtx and vhea/vmtx pairs rebuild over per-glyph
/// remapped values at the minimal storage boundary with the pairing re-validated
/// (OQ-SC-4). Every model forms before any table is published, so a failure still
/// refuses with nothing on the wire.</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildCompactGlyfAndLoca(GlyfTable glyfModel, LocaTable loca,
        GlyphRemap map, bool dropHinting, BuildBudget budget, out byte[]? glyfBytes,
        out byte[]? locaBytes, out FontDiagnostic diagnostic)
    {
        glyfBytes = null;
        locaBytes = null;
        ReadStatus status = GlyfTableCompiler.TryCompactSubset(glyfModel,
            map.OutputToInput, map, !loca.LongFormat, dropHinting, budget,
            out var image, out var offsets, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocaTableCompiler.TryFromOffsets(loca.LongFormat, loca.Location,
            loca.HeadLocation, loca.MaxpLocation, offsets!, out var rebuiltLoca,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocaTableCompiler.TryCompile(rebuiltLoca!, out locaBytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        glyfBytes = image;
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildMaxp(MaxpTable maxp, int outputGlyphCount,
        BuildBudget budget, out byte[]? maxpBytes, out FontDiagnostic diagnostic)
    {
        maxpBytes = null;
        // The 1.0 maxima keep their original values: the output glyph set is a subset of
        // the input set, so they stay upper bounds, and the report carries the explicit
        // not-recomputed fact (OQ-SC-1).
        ReadStatus status = maxp switch
        {
            Maxp10Table v10 => v10.TryWith(v10.Values with {
                NumGlyphs = checked((ushort)outputGlyphCount) }, out _, out diagnostic),
            Maxp05Table v05 => v05.TryWith(checked((ushort)outputGlyphCount), out _,
                out diagnostic),
            _ => throw new InvalidOperationException(
                "The admitted subset profile binds maxp 0.5 or 1.0 only."),
        };
        if (status != ReadStatus.Done) return status;
        return MaxpTableCompiler.TryCompile(maxp, out maxpBytes, out diagnostic, budget);
    }

    private static ReadStatus RebuildHorizontalMetrics(SfntDirectoryView directory,
        MaxpTable maxp, GlyphRemap map, BuildBudget budget, out byte[]? hheaBytes,
        out byte[]? hmtxBytes, out FontDiagnostic diagnostic)
    {
        hheaBytes = null;
        hmtxBytes = null;
        ReadStatus status = ReadTableBytes(directory, Hhea, out var hheaAt,
            out var hheaSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryOpen(hheaSource, out var hheaView, out diagnostic, hheaAt);
        if (status != ReadStatus.Done) return status;
        status = hheaView.TryRead(out HheaTable? hhea, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Hmtx, out var hmtxAt, out var hmtxSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HmtxContext.TryCreate(hhea!, maxp, hmtxAt, out var context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HmtxView.TryOpen(hmtxSource, out var hmtxView, out diagnostic, hmtxAt);
        if (status != ReadStatus.Done) return status;
        status = hmtxView.TryRead(context!, out HmtxTable? hmtx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        // Output o carries the input metrics of outputToInput[o]; the minimal boundary
        // is the final run of equal advances (OQ-SC-4), verified with the sharing check
        // TryReencode uses, and the pair re-validates before either half compiles.
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, hmtxAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var metrics = new HorizontalMetric[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            status = hmtx!.TryGetMetric(inputOrder[o], out metrics[o], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        int boundary = MinimalMetricsBoundary(metrics);
        var longMetrics = metrics[..boundary];
        var tail = new short[outputCount - boundary];
        for (int i = 0; i < tail.Length; i++)
            tail[i] = metrics[boundary + i].LeftSideBearing;
        var rebuiltHmtx = new HmtxTable(longMetrics, tail, hmtxAt, hhea!.Location,
            maxp.Location);
        // The TryReencode sharing check re-run as the boundary legality re-verifier.
        ushort sharedAdvance = rebuiltHmtx.MetricAt(boundary - 1).AdvanceWidth;
        for (int gid = boundary; gid < outputCount; gid++)
            if (rebuiltHmtx.MetricAt(gid).AdvanceWidth != sharedAdvance)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    rebuiltHmtx.Location.At(DataField.Payload, 0, gid), out diagnostic,
                    rebuiltHmtx.MetricAt(gid).AdvanceWidth, sharedAdvance);
        status = hhea.TryWith(hhea.Values with
        {
            NumberOfHMetrics = checked((ushort)boundary),
            AdvanceWidthMax = rebuiltHmtx.AdvanceWidthMax,
        }, out var rebuiltHhea, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HorizontalMetricsValidation.ValidatePair(rebuiltHhea!, rebuiltHmtx,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HheaTableCompiler.TryCompile(rebuiltHhea!, out hheaBytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        return HmtxTableCompiler.TryCompile(rebuiltHmtx, out hmtxBytes, out diagnostic,
            budget);
    }

    private static ReadStatus RebuildVerticalMetrics(SfntDirectoryView directory,
        MaxpTable maxp, GlyphRemap map, BuildBudget budget, out byte[]? vheaBytes,
        out byte[]? vmtxBytes, out FontDiagnostic diagnostic)
    {
        vheaBytes = null;
        vmtxBytes = null;
        ReadStatus status = ReadTableBytes(directory, Vhea, out var vheaAt,
            out var vheaSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryOpen(vheaSource, out var vheaView, out diagnostic, vheaAt);
        if (status != ReadStatus.Done) return status;
        status = vheaView.TryRead(out VheaTable? vhea, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Vmtx, out var vmtxAt, out var vmtxSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VmtxContext.TryCreate(vhea!, maxp, vmtxAt, out var context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VmtxView.TryOpen(vmtxSource, out var vmtxView, out diagnostic, vmtxAt);
        if (status != ReadStatus.Done) return status;
        status = vmtxView.TryRead(context!, out VmtxTable? vmtx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        // Same shape as the horizontal pair, over advance heights and top bearings.
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, vmtxAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var metrics = new VerticalMetric[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            status = vmtx!.TryGetMetric(inputOrder[o], out metrics[o], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        int boundary = MinimalMetricsBoundary(metrics);
        var longMetrics = metrics[..boundary];
        var tail = new short[outputCount - boundary];
        for (int i = 0; i < tail.Length; i++)
            tail[i] = metrics[boundary + i].TopSideBearing;
        var rebuiltVmtx = new VmtxTable(longMetrics, tail, vmtxAt, vhea!.Location,
            maxp.Location);
        ushort sharedHeight = rebuiltVmtx.MetricAt(boundary - 1).AdvanceHeight;
        for (int gid = boundary; gid < outputCount; gid++)
            if (rebuiltVmtx.MetricAt(gid).AdvanceHeight != sharedHeight)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    rebuiltVmtx.Location.At(DataField.Payload, 0, gid), out diagnostic,
                    rebuiltVmtx.MetricAt(gid).AdvanceHeight, sharedHeight);
        status = vhea.TryWith(vhea.Values with
        {
            NumOfLongVerMetrics = checked((ushort)boundary),
            AdvanceHeightMax = rebuiltVmtx.AdvanceHeightMax,
        }, out var rebuiltVhea, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VerticalMetricsValidation.ValidatePair(rebuiltVhea!, rebuiltVmtx,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VheaTableCompiler.TryCompile(rebuiltVhea!, out vheaBytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        return VmtxTableCompiler.TryCompile(rebuiltVmtx, out vmtxBytes, out diagnostic,
            budget);
    }

    /// <summary>The minimal storage boundary: the final run of equal advances, so every
    /// glyph at or past the boundary shares the last long record (the hmtx/vmtx tail
    /// rule). A single-glyph domain keeps the boundary at 1.</summary>
    private static int MinimalMetricsBoundary(ReadOnlySpan<HorizontalMetric> metrics)
    {
        int boundary = metrics.Length;
        while (boundary > 1 && metrics[boundary - 1].AdvanceWidth ==
            metrics[boundary - 2].AdvanceWidth)
            boundary--;
        return boundary;
    }

    private static int MinimalMetricsBoundary(ReadOnlySpan<VerticalMetric> metrics)
    {
        int boundary = metrics.Length;
        while (boundary > 1 && metrics[boundary - 1].AdvanceHeight ==
            metrics[boundary - 2].AdvanceHeight)
            boundary--;
        return boundary;
    }

    private static ReadStatus ReadTableBytes(SfntDirectoryView directory, uint tag,
        out DataLocation location, out ReadOnlySpan<byte> bytes,
        out FontDiagnostic diagnostic)
    {
        location = default;
        bytes = default;
        ReadStatus status = directory.TryFindUniqueTable(tag, out int ordinal,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ordinal, out var tableBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        location = directory.TableLocation(ordinal);
        bytes = tableBytes;
        return ReadStatus.Done;
    }
}
