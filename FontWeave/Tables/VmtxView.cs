using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>External count semantics over schema-generated storage, binding and local records.</summary>
public readonly ref partial struct VmtxView
{
    public ReadStatus TryGetLongMetric(VmtxContext context, int ordinal, out LongMetricData record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= context.NumOfLongVerMetrics)
            return InvalidIndex(ordinal, context.NumOfLongVerMetrics, out diagnostic);
        return TryReadLongMetricAt((ulong)ordinal * VmtxLayout.LongStride, ordinal, out record, out diagnostic);
    }

    public ReadStatus TryGetTailBearing(VmtxContext context, int ordinal, out TailBearingData record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = context.NumGlyphs - context.NumOfLongVerMetrics;
        if (ordinal < 0 || ordinal >= count) return InvalidIndex(ordinal, count, out diagnostic);
        return TryReadTailBearingAt((ulong)context.NumOfLongVerMetrics * VmtxLayout.LongStride +
            (ulong)ordinal * VmtxLayout.TailStride, ordinal, out record, out diagnostic);
    }

    public ReadStatus TryGetMetric(VmtxContext context, int glyphId, out VerticalMetric metric,
        out FontDiagnostic diagnostic)
    {
        metric = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < 0 || glyphId >= context.NumGlyphs) return InvalidIndex(glyphId, context.NumGlyphs, out diagnostic);
        status = TryGetLongMetric(context, Math.Min(glyphId, context.NumOfLongVerMetrics - 1), out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < context.NumOfLongVerMetrics) metric = new(record.AdvanceHeight, record.TopSideBearing);
        else
        {
            status = TryGetTailBearing(context, glyphId - context.NumOfLongVerMetrics, out var tail, out diagnostic);
            if (status != ReadStatus.Done) return status;
            metric = new(record.AdvanceHeight, tail.TopSideBearing);
        }
        return ReadStatus.Done;
    }

    public ReadStatus Validate(VmtxContext context, out FontDiagnostic diagnostic)
    {
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong expected = 2UL * ((ulong)context.NumGlyphs + context.NumOfLongVerMetrics);
        if ((ulong)_data.Length < expected)
        {
            int longs = context.NumOfLongVerMetrics * VmtxLayout.LongStride;
            return _data.Length < longs
                ? TryGetLongMetric(context, _data.Length / VmtxLayout.LongStride, out _, out diagnostic)
                : TryGetTailBearing(context, (_data.Length - longs) / VmtxLayout.TailStride, out _, out diagnostic);
        }
        if ((ulong)_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, expected), out diagnostic, (ulong)_data.Length, expected);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(VmtxContext context, out VmtxTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(context, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(VmtxContext context, out VmtxTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var metrics = new VerticalMetric[context.NumOfLongVerMetrics];
        var tail = new short[context.NumGlyphs - context.NumOfLongVerMetrics];
        for (int i = 0; i < metrics.Length; i++)
        {
            _ = TryGetLongMetric(context, i, out var record, out _);
            metrics[i] = new(record.AdvanceHeight, record.TopSideBearing);
        }
        for (int i = 0; i < tail.Length; i++)
        {
            _ = TryGetTailBearing(context, i, out var record, out _);
            tail[i] = record.TopSideBearing;
        }
        model = new(metrics, tail, Location, context.VheaLocation, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireContext(VmtxContext context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.Count, 0), out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InvalidIndex(int ordinal, int count, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.At(DataField.GlyphId, 0, ordinal), out diagnostic,
            unchecked((ulong)(long)ordinal), (ulong)count);
}
