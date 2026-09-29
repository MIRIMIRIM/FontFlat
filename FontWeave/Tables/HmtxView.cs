using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>External count semantics over schema-generated storage, binding and local records.</summary>
public readonly ref partial struct HmtxView
{
    public ReadStatus TryGetLongMetric(HmtxContext context, int ordinal, out LongMetricData record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= context.NumberOfHMetrics)
            return InvalidIndex(ordinal, context.NumberOfHMetrics, out diagnostic);
        return TryReadLongMetricAt((ulong)ordinal * HmtxLayout.LongStride, ordinal, out record, out diagnostic);
    }

    public ReadStatus TryGetTailBearing(HmtxContext context, int ordinal, out TailBearingData record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = context.NumGlyphs - context.NumberOfHMetrics;
        if (ordinal < 0 || ordinal >= count) return InvalidIndex(ordinal, count, out diagnostic);
        return TryReadTailBearingAt((ulong)context.NumberOfHMetrics * HmtxLayout.LongStride +
            (ulong)ordinal * HmtxLayout.TailStride, ordinal, out record, out diagnostic);
    }

    public ReadStatus TryGetMetric(HmtxContext context, int glyphId, out HorizontalMetric metric,
        out FontDiagnostic diagnostic)
    {
        metric = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < 0 || glyphId >= context.NumGlyphs) return InvalidIndex(glyphId, context.NumGlyphs, out diagnostic);
        status = TryGetLongMetric(context, Math.Min(glyphId, context.NumberOfHMetrics - 1), out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < context.NumberOfHMetrics) metric = new(record.AdvanceWidth, record.LeftSideBearing);
        else
        {
            status = TryGetTailBearing(context, glyphId - context.NumberOfHMetrics, out var tail, out diagnostic);
            if (status != ReadStatus.Done) return status;
            metric = new(record.AdvanceWidth, tail.LeftSideBearing);
        }
        return ReadStatus.Done;
    }

    public ReadStatus Validate(HmtxContext context, out FontDiagnostic diagnostic)
    {
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong expected = 2UL * ((ulong)context.NumGlyphs + context.NumberOfHMetrics);
        if ((ulong)_data.Length < expected)
        {
            int longs = context.NumberOfHMetrics * HmtxLayout.LongStride;
            return _data.Length < longs
                ? TryGetLongMetric(context, _data.Length / HmtxLayout.LongStride, out _, out diagnostic)
                : TryGetTailBearing(context, (_data.Length - longs) / HmtxLayout.TailStride, out _, out diagnostic);
        }
        if ((ulong)_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, expected), out diagnostic, (ulong)_data.Length, expected);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(HmtxContext context, out HmtxTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(context, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(HmtxContext context, out HmtxTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var metrics = new HorizontalMetric[context.NumberOfHMetrics];
        var tail = new short[context.NumGlyphs - context.NumberOfHMetrics];
        for (int i = 0; i < metrics.Length; i++)
        {
            _ = TryGetLongMetric(context, i, out var record, out _);
            metrics[i] = new(record.AdvanceWidth, record.LeftSideBearing);
        }
        for (int i = 0; i < tail.Length; i++)
        {
            _ = TryGetTailBearing(context, i, out var record, out _);
            tail[i] = record.LeftSideBearing;
        }
        model = new(metrics, tail, Location, context.HheaLocation, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireContext(HmtxContext context, out FontDiagnostic diagnostic)
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
