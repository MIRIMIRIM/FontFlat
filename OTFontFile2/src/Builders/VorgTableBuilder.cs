using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>VORG</c> table.
/// </summary>
[OtTableBuilder("VORG")]
public sealed partial class VorgTableBuilder : ISfntTableSource
{
    private readonly List<VertOriginMetric> _metrics = new();

    private ushort _majorVersion = 1;
    private ushort _minorVersion;
    private short _defaultVertOriginY;

    public ushort MajorVersion
    {
        get => _majorVersion;
        set
        {
            if (value == _majorVersion)
                return;

            _majorVersion = value;
            MarkDirty();
        }
    }

    public ushort MinorVersion
    {
        get => _minorVersion;
        set
        {
            if (value == _minorVersion)
                return;

            _minorVersion = value;
            MarkDirty();
        }
    }

    public short DefaultVertOriginY
    {
        get => _defaultVertOriginY;
        set
        {
            if (value == _defaultVertOriginY)
                return;

            _defaultVertOriginY = value;
            MarkDirty();
        }
    }

    public int MetricCount => _metrics.Count;

    public IReadOnlyList<VertOriginMetric> Metrics => _metrics;

    public void ClearMetrics()
    {
        if (_metrics.Count == 0)
            return;

        _metrics.Clear();
        MarkDirty();
    }

    public void AddMetric(ushort glyphIndex, short vertOriginY)
    {
        _metrics.Add(new VertOriginMetric(glyphIndex, vertOriginY));
        MarkDirty();
    }

    public void AddOrReplaceMetric(ushort glyphIndex, short vertOriginY)
    {
        for (int i = 0; i < _metrics.Count; i++)
        {
            if (_metrics[i].GlyphIndex == glyphIndex)
            {
                _metrics[i] = new VertOriginMetric(glyphIndex, vertOriginY);
                MarkDirty();
                return;
            }
        }

        _metrics.Add(new VertOriginMetric(glyphIndex, vertOriginY));
        MarkDirty();
    }

    public bool RemoveMetric(ushort glyphIndex)
    {
        for (int i = _metrics.Count - 1; i >= 0; i--)
        {
            if (_metrics[i].GlyphIndex == glyphIndex)
            {
                _metrics.RemoveAt(i);
                MarkDirty();
                return true;
            }
        }

        return false;
    }

    public bool TryGetMetric(ushort glyphIndex, out short vertOriginY)
    {
        for (int i = 0; i < _metrics.Count; i++)
        {
            var m = _metrics[i];
            if (m.GlyphIndex == glyphIndex)
            {
                vertOriginY = m.VertOriginY;
                return true;
            }
        }

        vertOriginY = 0;
        return false;
    }

    public static bool TryFrom(VorgTable vorg, out VorgTableBuilder builder)
    {
        builder = new VorgTableBuilder
        {
            MajorVersion = vorg.MajorVersion,
            MinorVersion = vorg.MinorVersion,
            DefaultVertOriginY = vorg.DefaultVertOriginY
        };

        int count = vorg.MetricCount;
        for (int i = 0; i < count; i++)
        {
            if (!vorg.TryGetMetric(i, out var metric))
                return false;

            builder._metrics.Add(new VertOriginMetric(metric.GlyphIndex, metric.VertOriginY));
        }

        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if (_metrics.Count > ushort.MaxValue)
            throw new InvalidOperationException("VORG metric count must fit in uint16.");

        _metrics.Sort(static (a, b) => a.GlyphIndex.CompareTo(b.GlyphIndex));

        // Header: major(2) minor(2) defaultVertOriginY(2) numMetrics(2)
        int count = _metrics.Count;
        int length = checked(8 + (count * 4));

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, MajorVersion);
        BigEndian.WriteUInt16(span, 2, MinorVersion);
        BigEndian.WriteInt16(span, 4, DefaultVertOriginY);
        BigEndian.WriteUInt16(span, 6, checked((ushort)count));

        int pos = 8;
        for (int i = 0; i < count; i++)
        {
            var m = _metrics[i];
            BigEndian.WriteUInt16(span, pos + 0, m.GlyphIndex);
            BigEndian.WriteInt16(span, pos + 2, m.VertOriginY);
            pos += 4;
        }

        return table;
    }

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
}
