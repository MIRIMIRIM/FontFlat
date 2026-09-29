using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>All vhea wire values, without recomputing outline-derived extrema.
/// Ascender/Descender/LineGap are ascent/descent/reserved in 1.0 and the vertical
/// typographic ascender/descender/gap in 1.1; VersionRaw preserves that distinction.</summary>
public readonly record struct VheaValues(uint VersionRaw,
    short Ascender, short Descender, short LineGap, ushort AdvanceHeightMax,
    short MinTopSideBearing, short MinBottomSideBearing, short YMaxExtent,
    short CaretSlopeRise, short CaretSlopeRun, short CaretOffset,
    short Reserved1, short Reserved2, short Reserved3, short Reserved4,
    short MetricDataFormat, ushort NumOfLongVerMetrics)
{
    public bool UsesTypographicMetrics => VersionRaw == VheaLayout.Version11;
}

public sealed class VheaTable
{
    internal VheaTable(VheaValues values, DataLocation location, bool hasOriginalMetricCount = true)
    { Values = values; Location = location; HasOriginalMetricCount = hasOriginalMetricCount; }

    public VheaValues Values { get; }
    public DataLocation Location { get; }
    internal bool HasOriginalMetricCount { get; }

    public ReadStatus TryWith(VheaValues values, out VheaTable? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = VheaView.ValidateValues(values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(values, Location,
            HasOriginalMetricCount && values.NumOfLongVerMetrics == Values.NumOfLongVerMetrics);
        return ReadStatus.Done;
    }
}
