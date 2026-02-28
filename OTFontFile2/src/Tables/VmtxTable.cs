using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("vmtx", 0, GenerateTryCreate = false)]
public readonly partial struct VmtxTable
{
    public static bool TryCreate(TableSlice table, out VmtxTable vmtx)
    {
        vmtx = new VmtxTable(table);
        return table.Length != 0;
    }

    public readonly struct LongVerMetric
    {
        public ushort AdvanceHeight { get; }
        public short TopSideBearing { get; }

        public LongVerMetric(ushort advanceHeight, short topSideBearing)
        {
            AdvanceHeight = advanceHeight;
            TopSideBearing = topSideBearing;
        }
    }

    public bool TryGetMetric(ushort glyphId, ushort numOfLongVerMetrics, ushort numGlyphs, out LongVerMetric metric)
    {
        metric = default;

        if (glyphId >= numGlyphs || numOfLongVerMetrics == 0 || numOfLongVerMetrics > numGlyphs)
            return false;

        var data = _table.Span;
        int fullMetricsBytes = numOfLongVerMetrics * 4;

        if (data.Length < 4 || data.Length < fullMetricsBytes)
            return false;

        if (glyphId < numOfLongVerMetrics)
        {
            int o = glyphId * 4;
            if ((uint)o > (uint)data.Length - 4)
                return false;

            metric = new LongVerMetric(
                advanceHeight: BigEndian.ReadUInt16(data, o),
                topSideBearing: BigEndian.ReadInt16(data, o + 2));
            return true;
        }

        int lastMetricOffset = (numOfLongVerMetrics - 1) * 4;
        if ((uint)lastMetricOffset > (uint)data.Length - 4)
            return false;

        ushort advanceHeightMax = BigEndian.ReadUInt16(data, lastMetricOffset);

        int tsbIndex = glyphId - numOfLongVerMetrics;
        int tsbOffset = fullMetricsBytes + (tsbIndex * 2);
        if ((uint)tsbOffset > (uint)data.Length - 2)
            return false;

        short tsb = BigEndian.ReadInt16(data, tsbOffset);
        metric = new LongVerMetric(advanceHeightMax, tsb);
        return true;
    }
}
