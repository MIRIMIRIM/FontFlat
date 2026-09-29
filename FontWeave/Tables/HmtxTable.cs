using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct HorizontalMetric(ushort AdvanceWidth, short LeftSideBearing);

/// <summary>Owned metrics retaining the original long/tail storage boundary.</summary>
public sealed class HmtxTable
{
    private readonly HorizontalMetric[] _longMetrics;
    private readonly short[] _tail;

    internal HmtxTable(HorizontalMetric[] longMetrics, short[] tail, DataLocation location,
        DataLocation hheaLocation, DataLocation maxpLocation)
    {
        _longMetrics = longMetrics; _tail = tail; Location = location;
        HheaLocation = hheaLocation; MaxpLocation = maxpLocation;
    }

    public ushort NumGlyphs => (ushort)(_longMetrics.Length + _tail.Length);
    public ushort NumberOfHMetrics => (ushort)_longMetrics.Length;
    public ReadOnlySpan<HorizontalMetric> LongMetrics => _longMetrics;
    public ReadOnlySpan<short> TailLeftSideBearings => _tail;
    public DataLocation Location { get; }
    public DataLocation HheaLocation { get; }
    public DataLocation MaxpLocation { get; }

    public ReadStatus TryGetMetric(int glyphId, out HorizontalMetric metric,
        out FontDiagnostic diagnostic)
    {
        metric = default;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), NumGlyphs);
        metric = MetricAt(glyphId);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal HorizontalMetric MetricAt(int glyphId) => glyphId < _longMetrics.Length
        ? _longMetrics[glyphId]
        : new(_longMetrics[^1].AdvanceWidth, _tail[glyphId - _longMetrics.Length]);

    public ushort AdvanceWidthMax
    {
        get
        {
            ushort maximum = 0;
            foreach (var metric in _longMetrics) maximum = Math.Max(maximum, metric.AdvanceWidth);
            return maximum;
        }
    }
}

/// <summary>Named table-level values. A last-long AW edit intentionally affects every tail glyph.</summary>
public readonly record struct HorizontalMetricEdit(int GlyphId, int? AdvanceWidth = null,
    int? LeftSideBearing = null);

public sealed record HmtxEditPlan
{
    public IReadOnlyList<HorizontalMetricEdit> Metrics { get; init; } = [];
}

public static class HorizontalMetricsValidation
{
    /// <summary>Checks counts and AW maximum, not outline-dependent extrema or sidebearing relations.</summary>
    public static ReadStatus ValidatePair(HheaTable hhea, HmtxTable hmtx,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(hhea);
        ArgumentNullException.ThrowIfNull(hmtx);
        if (!HmtxContext.SameFace(hhea.Location, hmtx.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                hmtx.Location, out diagnostic);
        if (hhea.Values.NumberOfHMetrics != hmtx.NumberOfHMetrics)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                hhea.Location.At(DataField.Count, 34), out diagnostic,
                hhea.Values.NumberOfHMetrics, hmtx.NumberOfHMetrics);
        ushort maximum = hmtx.AdvanceWidthMax;
        if (hhea.Values.AdvanceWidthMax != maximum)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                hhea.Location.At(DataField.Payload, 10), out diagnostic,
                hhea.Values.AdvanceWidthMax, maximum);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
