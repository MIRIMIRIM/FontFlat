using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>All eighteen hhea wire values, without recomputing outline-derived extrema.</summary>
public readonly record struct HheaValues(ushort MajorVersion, ushort MinorVersion,
    short Ascender, short Descender, short LineGap, ushort AdvanceWidthMax,
    short MinLeftSideBearing, short MinRightSideBearing, short XMaxExtent,
    short CaretSlopeRise, short CaretSlopeRun, short CaretOffset,
    short Reserved1, short Reserved2, short Reserved3, short Reserved4,
    short MetricDataFormat, ushort NumberOfHMetrics);

public sealed class HheaTable
{
    internal HheaTable(HheaValues values, DataLocation location, bool hasOriginalMetricCount = true)
    { Values = values; Location = location; HasOriginalMetricCount = hasOriginalMetricCount; }

    public HheaValues Values { get; }
    public DataLocation Location { get; }
    internal bool HasOriginalMetricCount { get; }

    public ReadStatus TryWith(HheaValues values, out HheaTable? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = HheaView.ValidateValues(values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(values, Location,
            HasOriginalMetricCount && values.NumberOfHMetrics == Values.NumberOfHMetrics);
        return ReadStatus.Done;
    }
}
