using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("VORG", 8)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("DefaultVertOriginY", OtFieldKind.Int16, 4)]
[OtField("MetricCount", OtFieldKind.UInt16, 6)]
[OtSequentialRecordArray("Metric", 8, 4, RecordTypeName = "VertOriginMetric")]
public readonly partial struct VorgTable
{
    public readonly struct VertOriginMetric
    {
        public ushort GlyphIndex { get; }
        public short VertOriginY { get; }

        public VertOriginMetric(ushort glyphIndex, short vertOriginY)
        {
            GlyphIndex = glyphIndex;
            VertOriginY = vertOriginY;
        }
    }

    public bool TryGetVertOriginY(ushort glyphIndex, out short vertOriginY)
    {
        vertOriginY = DefaultVertOriginY;

        int count = MetricCount;
        if (count == 0)
            return true;

        int lo = 0;
        int hi = count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (!TryGetMetric(mid, out var metric))
                return false;

            if (glyphIndex < metric.GlyphIndex)
            {
                hi = mid - 1;
                continue;
            }

            if (glyphIndex > metric.GlyphIndex)
            {
                lo = mid + 1;
                continue;
            }

            vertOriginY = metric.VertOriginY;
            return true;
        }

        return true;
    }
}
