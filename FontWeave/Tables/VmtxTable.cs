using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct VerticalMetric(ushort AdvanceHeight, short TopSideBearing);

/// <summary>Owned metrics retaining the original long/tail storage boundary.</summary>
public sealed class VmtxTable
{
    private readonly VerticalMetric[] _longMetrics;
    private readonly short[] _tail;

    internal VmtxTable(VerticalMetric[] longMetrics, short[] tail, DataLocation location,
        DataLocation vheaLocation, DataLocation maxpLocation)
    {
        _longMetrics = longMetrics; _tail = tail; Location = location;
        VheaLocation = vheaLocation; MaxpLocation = maxpLocation;
    }

    public ushort NumGlyphs => (ushort)(_longMetrics.Length + _tail.Length);
    public ushort NumOfLongVerMetrics => (ushort)_longMetrics.Length;
    public ReadOnlySpan<VerticalMetric> LongMetrics => _longMetrics;
    public ReadOnlySpan<short> TailTopSideBearings => _tail;
    public DataLocation Location { get; }
    public DataLocation VheaLocation { get; }
    public DataLocation MaxpLocation { get; }

    public ReadStatus TryGetMetric(int glyphId, out VerticalMetric metric,
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

    internal VerticalMetric MetricAt(int glyphId) => glyphId < _longMetrics.Length
        ? _longMetrics[glyphId]
        : new(_longMetrics[^1].AdvanceHeight, _tail[glyphId - _longMetrics.Length]);

    public ushort AdvanceHeightMax
    {
        get
        {
            ushort maximum = 0;
            foreach (var metric in _longMetrics) maximum = Math.Max(maximum, metric.AdvanceHeight);
            return maximum;
        }
    }
}

/// <summary>Named table-level values. A last-long AH edit intentionally affects every tail glyph.</summary>
public readonly record struct VerticalMetricEdit(int GlyphId, int? AdvanceHeight = null,
    int? TopSideBearing = null);

public sealed record VmtxEditPlan
{
    public IReadOnlyList<VerticalMetricEdit> Metrics { get; init; } = [];
}

public static class VerticalMetricsValidation
{
    /// <summary>Checks counts and AH maximum, not outline-dependent extrema or sidebearing relations.</summary>
    public static ReadStatus ValidatePair(VheaTable vhea, VmtxTable vmtx,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(vhea);
        ArgumentNullException.ThrowIfNull(vmtx);
        if (!VmtxContext.SameFace(vhea.Location, vmtx.Location) ||
            (vmtx.Location.SourceId != 0 && vmtx.VheaLocation != vhea.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                vmtx.Location, out diagnostic);
        if (vhea.Values.NumOfLongVerMetrics != vmtx.NumOfLongVerMetrics)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                vhea.Location.At(DataField.Count, 34), out diagnostic,
                vhea.Values.NumOfLongVerMetrics, vmtx.NumOfLongVerMetrics);
        ushort maximum = vmtx.AdvanceHeightMax;
        if (vhea.Values.AdvanceHeightMax != maximum)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                vhea.Location.At(DataField.Payload, 10), out diagnostic,
                vhea.Values.AdvanceHeightMax, maximum);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
