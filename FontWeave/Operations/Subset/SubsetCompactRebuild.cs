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
        // not-recomputed fact (OQ-SC-1). Only numGlyphs moves to the map output domain —
        // and the REBUILT model is what compiles, not the input one.
        MaxpTable rebuilt;
        ReadStatus status;
        switch (maxp)
        {
            case Maxp10Table v10:
                status = v10.TryWith(v10.Values with {
                    NumGlyphs = checked((ushort)outputGlyphCount) }, out var r10,
                    out diagnostic);
                rebuilt = r10!;
                break;
            case Maxp05Table v05:
                status = v05.TryWith(checked((ushort)outputGlyphCount), out var r05,
                    out diagnostic);
                rebuilt = r05!;
                break;
            default:
                throw new InvalidOperationException(
                    "The admitted subset profile binds maxp 0.5 or 1.0 only.");
        }
        if (status != ReadStatus.Done) return status;
        return MaxpTableCompiler.TryCompile(rebuilt, out maxpBytes, out diagnostic,
            budget);
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


    private static ReadStatus RebuildPost20(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? postBytes, out FontDiagnostic diagnostic)
    {
        postBytes = null;
        ReadStatus status = ReadTableBytes(directory, Post, out var postAt,
            out var postSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = PostView.TryOpen(postSource, out var postView, out diagnostic, postAt);
        if (status != ReadStatus.Done) return status;
        status = postView.TryRead(out PostTable? post, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // The rebuild indexes glyphNameIndex over the whole input domain, so the post
        // count must cover it exactly (the maxp-bound read the metrics contexts do).
        if (post!.NumGlyphs!.Value != maxp.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                postAt.At(DataField.Count, 32), out diagnostic,
                (ulong)post.NumGlyphs.Value, (ulong)maxp.NumGlyphs);
        // glyphNameIndex remaps through the frozen map; custom names prune to the
        // referenced set, renumbered 258+i in their original relative order (OQ-SC-2),
        // so every output glyph resolves to exactly the name its input glyph had.
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, postAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var oldIndices = post!.GlyphNameIndices;
        var referenced = new List<string>();
        var renamed = new Dictionary<int, ushort>();
        for (int o = 0; o < outputCount; o++)
        {
            int old = oldIndices[inputOrder[o]];
            if (old >= 258 && !renamed.ContainsKey(old))
            {
                if (renamed.Count > ushort.MaxValue - 258)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        postAt.At(DataField.Count, 34), out diagnostic,
                        (ulong)renamed.Count, ushort.MaxValue);
                renamed[old] = (ushort)(258 + referenced.Count);
                referenced.Add(post.CustomNames[old - 258]);
            }
        }
        // One rewrite per output slot against the completed rename table: renamed values
        // share the >=258 domain with original indices, so an incremental pass could
        // rename an already-renamed slot again.
        var newIndices = new ushort[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            int old = oldIndices[inputOrder[o]];
            newIndices[o] = old >= 258 && renamed.TryGetValue(old, out ushort newName)
                ? newName
                : (ushort)old;
        }
        var rebuilt = new PostTable(post.Version, post.Header, newIndices, [],
            referenced.ToArray(), postAt);
        return PostTableCompiler.TryCompile(rebuilt, out postBytes, out diagnostic, budget);
    }

    private static ReadStatus RebuildHdmx(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? hdmxBytes,
        out FontDiagnostic diagnostic)
    {
        hdmxBytes = null;
        ReadStatus status = ReadTableBytes(directory, Hdmx, out var hdmxAt,
            out var hdmxSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LegacyMetricsContext.TryCreate(maxp, hdmxAt, out var context,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HdmxView.TryOpen(hdmxSource, out var hdmxView, out diagnostic, hdmxAt);
        if (status != ReadStatus.Done) return status;
        status = hdmxView.TryRead(context!, out HdmxTable? hdmx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Each record's widths array reorders through the frozen map; the PixelSize set
        // and record order stay untouched, and compilation recomputes MaxWidth and the
        // canonical stride over the output domain.
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work((long)hdmx!.NumRecords * outputCount, hdmxAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var records = new HdmxDeviceRecord[hdmx.NumRecords];
        for (int r = 0; r < records.Length; r++)
        {
            var source = hdmx.Records[r];
            var widths = new byte[outputCount];
            for (int o = 0; o < outputCount; o++)
                widths[o] = source.Widths[inputOrder[o]];
            records[r] = new(source.PixelSize, MaximumWidth(widths), widths);
        }
        var rebuilt = new HdmxTable(checked((ushort)outputCount),
            hdmx.SourceSizeDeviceRecord, records, hdmxAt, maxp.Location);
        return HdmxTableCompiler.TryCompile(rebuilt, out hdmxBytes, out diagnostic, budget);
    }

    private static ReadStatus RebuildLtsh(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? ltshBytes,
        out FontDiagnostic diagnostic)
    {
        ltshBytes = null;
        ReadStatus status = ReadTableBytes(directory, Ltsh, out var ltshAt,
            out var ltshSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LegacyMetricsContext.TryCreate(maxp, ltshAt, out var context,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LtshView.TryOpen(ltshSource, out var ltshView, out diagnostic, ltshAt);
        if (status != ReadStatus.Done) return status;
        status = ltshView.TryRead(context!, out LtshTable? ltsh, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, ltshAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var thresholds = new byte[outputCount];
        for (int o = 0; o < outputCount; o++)
            thresholds[o] = ltsh!.YPixels[inputOrder[o]];
        var rebuilt = new LtshTable(thresholds, ltshAt, maxp.Location);
        return LtshTableCompiler.TryCompile(rebuilt, out ltshBytes, out diagnostic, budget);
    }

    /// <summary>VDMX carries no GID domain — its groups key on ratio/ppem and its
    /// entries on pixel height (SC card §2, wire fact). The rebuild is a typed
    /// re-emission: the model reads back verbatim and compilation re-pins the group
    /// offsets to the canonical layout, so the decoded ratio/group/entry sets stay
    /// semantically identical to the input.</summary>
    private static ReadStatus RebuildVdmx(SfntDirectoryView directory,
        BuildBudget budget, out byte[]? vdmxBytes, out FontDiagnostic diagnostic)
    {
        vdmxBytes = null;
        ReadStatus status = ReadTableBytes(directory, Vdmx, out var vdmxAt,
            out var vdmxSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VdmxView.TryOpen(vdmxSource, out var vdmxView, out diagnostic, vdmxAt);
        if (status != ReadStatus.Done) return status;
        status = vdmxView.TryRead(out VdmxTable? vdmx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(vdmx!.NumRatios + vdmx.NumGroups, vdmxAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var groups = new VdmxGroup[vdmx.NumGroups];
        ulong offset = (ulong)VdmxLayout.DirectoryEnd(vdmx.NumRatios);
        for (int g = 0; g < groups.Length; g++)
        {
            var source = vdmx.Groups[g];
            groups[g] = new(offset, source.StartSize, source.EndSize,
                source.Entries.ToArray());
            offset += (ulong)VdmxLayout.GroupLength(source.Entries.Length);
        }
        var rebuilt = new VdmxTable(vdmx.Version, vdmx.Ratios.ToArray(), groups, vdmxAt);
        return VdmxTableCompiler.TryCompile(rebuilt, out vdmxBytes, out diagnostic, budget);
    }

    private static byte MaximumWidth(ReadOnlySpan<byte> widths)
    {
        byte maximum = 0;
        foreach (byte width in widths)
            if (width > maximum) maximum = width;
        return maximum;
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
