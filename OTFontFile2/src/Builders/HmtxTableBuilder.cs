using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>hmtx</c> table.
/// </summary>
[OtTableBuilder("hmtx")]
public sealed partial class HmtxTableBuilder : ISfntTableSource
{
    private readonly ushort _numGlyphs;
    private ushort _numberOfHMetrics;

    private LongHorMetricEntry[] _metrics;
    private short[] _lsbs;

    public HmtxTableBuilder(ushort numGlyphs, ushort numberOfHMetrics)
    {
        if (numGlyphs == 0)
            throw new ArgumentOutOfRangeException(nameof(numGlyphs), "numGlyphs must be >= 1.");

        if (numberOfHMetrics == 0 || numberOfHMetrics > numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(numberOfHMetrics), "numberOfHMetrics must be in the range 1..numGlyphs.");

        _numGlyphs = numGlyphs;
        _numberOfHMetrics = numberOfHMetrics;

        _metrics = new LongHorMetricEntry[numberOfHMetrics];
        _lsbs = new short[numGlyphs - numberOfHMetrics];
    }

    public ushort NumGlyphs => _numGlyphs;

    public ushort NumberOfHMetrics => _numberOfHMetrics;

    public bool TryGetMetric(ushort glyphId, out ushort advanceWidth, out short leftSideBearing)
    {
        advanceWidth = 0;
        leftSideBearing = 0;

        if (glyphId >= _numGlyphs)
            return false;

        ushort n = _numberOfHMetrics;
        if (n == 0 || n > _numGlyphs)
            return false;

        if (glyphId < n)
        {
            var m = _metrics[glyphId];
            advanceWidth = m.AdvanceWidth;
            leftSideBearing = m.LeftSideBearing;
            return true;
        }

        advanceWidth = _metrics[n - 1].AdvanceWidth;
        int lsbIndex = glyphId - n;
        if ((uint)lsbIndex >= (uint)_lsbs.Length)
            return false;

        leftSideBearing = _lsbs[lsbIndex];
        return true;
    }

    public void SetMetric(ushort glyphId, ushort advanceWidth, short leftSideBearing)
    {
        if (glyphId >= _numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        ushort n = _numberOfHMetrics;
        if (n == 0 || n > _numGlyphs)
            throw new InvalidOperationException("hmtx builder is in an invalid state (numberOfHMetrics).");

        if (glyphId < n)
        {
            _metrics[glyphId] = new LongHorMetricEntry(advanceWidth, leftSideBearing);
            MarkDirty();
            return;
        }

        // For glyphs beyond numberOfHMetrics, advanceWidth is repeated from the last longHorMetric.
        ushort repeated = _metrics[n - 1].AdvanceWidth;
        if (advanceWidth != repeated)
        {
            EnsureFullMetrics();
            _metrics[glyphId] = new LongHorMetricEntry(advanceWidth, leftSideBearing);
            MarkDirty();
            return;
        }

        int lsbIndex = glyphId - n;
        if ((uint)lsbIndex >= (uint)_lsbs.Length)
            throw new InvalidOperationException("hmtx builder is in an invalid state (lsb array).");

        _lsbs[lsbIndex] = leftSideBearing;
        MarkDirty();
    }

    public void SetAdvanceWidth(ushort glyphId, ushort advanceWidth)
    {
        if (!TryGetMetric(glyphId, out _, out short lsb))
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        SetMetric(glyphId, advanceWidth, lsb);
    }

    public void SetLeftSideBearing(ushort glyphId, short leftSideBearing)
    {
        if (!TryGetMetric(glyphId, out ushort aw, out _))
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        SetMetric(glyphId, aw, leftSideBearing);
    }

    public void EnsureFullMetrics()
    {
        if (_numberOfHMetrics == _numGlyphs)
            return;

        ushort oldCount = _numberOfHMetrics;
        if (oldCount == 0 || oldCount > _numGlyphs)
            throw new InvalidOperationException("hmtx builder is in an invalid state (numberOfHMetrics).");

        var expanded = new LongHorMetricEntry[_numGlyphs];

        int oldCountInt = oldCount;
        _metrics.AsSpan(0, oldCountInt).CopyTo(expanded);

        ushort repeated = _metrics[oldCount - 1].AdvanceWidth;

        for (int i = oldCountInt; i < expanded.Length; i++)
        {
            int lsbIndex = i - oldCountInt;
            if ((uint)lsbIndex >= (uint)_lsbs.Length)
                throw new InvalidOperationException("hmtx builder is in an invalid state (lsb array).");

            expanded[i] = new LongHorMetricEntry(repeated, _lsbs[lsbIndex]);
        }

        _metrics = expanded;
        _lsbs = Array.Empty<short>();
        _numberOfHMetrics = _numGlyphs;
        MarkDirty();
    }

    public static bool TryFrom(HmtxTable hmtx, ushort numberOfHMetrics, ushort numGlyphs, out HmtxTableBuilder builder)
    {
        builder = null!;

        if (numGlyphs == 0)
            return false;

        if (numberOfHMetrics == 0 || numberOfHMetrics > numGlyphs)
            return false;

        int requiredLength = GetLength(numGlyphs, numberOfHMetrics);
        if (hmtx.Table.Length < requiredLength)
            return false;

        var data = hmtx.Table.Span;
        int fullMetricsBytes = numberOfHMetrics * 4;

        var b = new HmtxTableBuilder(numGlyphs, numberOfHMetrics);

        int offset = 0;
        for (int i = 0; i < numberOfHMetrics; i++)
        {
            b._metrics[i] = new LongHorMetricEntry(
                advanceWidth: BigEndian.ReadUInt16(data, offset),
                leftSideBearing: BigEndian.ReadInt16(data, offset + 2));
            offset += 4;
        }

        int lsbCount = numGlyphs - numberOfHMetrics;
        for (int i = 0; i < lsbCount; i++)
        {
            b._lsbs[i] = BigEndian.ReadInt16(data, fullMetricsBytes + (i * 2));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_metrics.Length != _numberOfHMetrics)
            throw new InvalidOperationException("hmtx builder is in an invalid state (metrics length).");

        if (_lsbs.Length != _numGlyphs - _numberOfHMetrics)
            throw new InvalidOperationException("hmtx builder is in an invalid state (lsb length).");

        int length = GetLength(_numGlyphs, _numberOfHMetrics);
        byte[] table = new byte[length];
        var span = table.AsSpan();

        int offset = 0;
        for (int i = 0; i < _metrics.Length; i++)
        {
            var m = _metrics[i];
            BigEndian.WriteUInt16(span, offset, m.AdvanceWidth);
            BigEndian.WriteInt16(span, offset + 2, m.LeftSideBearing);
            offset += 4;
        }

        for (int i = 0; i < _lsbs.Length; i++)
        {
            BigEndian.WriteInt16(span, offset, _lsbs[i]);
            offset += 2;
        }

        return table;
    }

    private static int GetLength(ushort numGlyphs, ushort numberOfHMetrics)
        => checked((numberOfHMetrics * 4) + ((numGlyphs - numberOfHMetrics) * 2));

    private readonly struct LongHorMetricEntry
    {
        public ushort AdvanceWidth { get; }
        public short LeftSideBearing { get; }

        public LongHorMetricEntry(ushort advanceWidth, short leftSideBearing)
        {
            AdvanceWidth = advanceWidth;
            LeftSideBearing = leftSideBearing;
        }
    }
}
