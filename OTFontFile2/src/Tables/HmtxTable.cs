using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("hmtx", 0, GenerateTryCreate = false)]
public readonly partial struct HmtxTable
{
    public static bool TryCreate(TableSlice table, out HmtxTable hmtx)
    {
        // Validation requires hhea.numberOfHMetrics and maxp.numGlyphs; keep it lightweight here.
        hmtx = new HmtxTable(table);
        return table.Length != 0;
    }

    public readonly struct LongHorMetric
    {
        public ushort AdvanceWidth { get; }
        public short LeftSideBearing { get; }

        public LongHorMetric(ushort advanceWidth, short leftSideBearing)
        {
            AdvanceWidth = advanceWidth;
            LeftSideBearing = leftSideBearing;
        }
    }

    public bool TryGetMetric(ushort glyphId, ushort numberOfHMetrics, ushort numGlyphs, out LongHorMetric metric)
    {
        metric = default;

        if (glyphId >= numGlyphs || numberOfHMetrics == 0 || numberOfHMetrics > numGlyphs)
            return false;

        var data = _table.Span;
        int fullMetricsBytes = numberOfHMetrics * 4;

        // Need at least one longHorMetric.
        if (data.Length < 4 || data.Length < fullMetricsBytes)
            return false;

        if (glyphId < numberOfHMetrics)
        {
            int o = glyphId * 4;
            if ((uint)o > (uint)data.Length - 4)
                return false;

            metric = new LongHorMetric(
                advanceWidth: BigEndian.ReadUInt16(data, o),
                leftSideBearing: BigEndian.ReadInt16(data, o + 2));
            return true;
        }

        // For glyphs beyond numberOfHMetrics:
        // - advanceWidth is from the last longHorMetric
        // - LSB comes from the trailing short array
        int lastMetricOffset = (numberOfHMetrics - 1) * 4;
        if ((uint)lastMetricOffset > (uint)data.Length - 4)
            return false;

        ushort advanceWidthMax = BigEndian.ReadUInt16(data, lastMetricOffset);

        int lsbIndex = glyphId - numberOfHMetrics;
        int lsbOffset = fullMetricsBytes + (lsbIndex * 2);
        if ((uint)lsbOffset > (uint)data.Length - 2)
            return false;

        short lsb = BigEndian.ReadInt16(data, lsbOffset);
        metric = new LongHorMetric(advanceWidthMax, lsb);
        return true;
    }
}
