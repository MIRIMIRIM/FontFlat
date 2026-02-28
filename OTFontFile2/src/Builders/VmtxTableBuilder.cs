using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>vmtx</c> table.
/// </summary>
[OtTableBuilder("vmtx")]
public sealed partial class VmtxTableBuilder : ISfntTableSource
{
    private readonly ushort _numGlyphs;
    private ushort _numOfLongVerMetrics;

    private LongVerMetricEntry[] _metrics;
    private short[] _tsbs;

    public VmtxTableBuilder(ushort numGlyphs, ushort numOfLongVerMetrics)
    {
        if (numGlyphs == 0)
            throw new ArgumentOutOfRangeException(nameof(numGlyphs), "numGlyphs must be >= 1.");

        if (numOfLongVerMetrics == 0 || numOfLongVerMetrics > numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(numOfLongVerMetrics), "numOfLongVerMetrics must be in the range 1..numGlyphs.");

        _numGlyphs = numGlyphs;
        _numOfLongVerMetrics = numOfLongVerMetrics;

        _metrics = new LongVerMetricEntry[numOfLongVerMetrics];
        _tsbs = new short[numGlyphs - numOfLongVerMetrics];
    }

    public ushort NumGlyphs => _numGlyphs;

    public ushort NumOfLongVerMetrics => _numOfLongVerMetrics;

    public bool TryGetMetric(ushort glyphId, out ushort advanceHeight, out short topSideBearing)
    {
        advanceHeight = 0;
        topSideBearing = 0;

        if (glyphId >= _numGlyphs)
            return false;

        ushort n = _numOfLongVerMetrics;
        if (n == 0 || n > _numGlyphs)
            return false;

        if (glyphId < n)
        {
            var m = _metrics[glyphId];
            advanceHeight = m.AdvanceHeight;
            topSideBearing = m.TopSideBearing;
            return true;
        }

        advanceHeight = _metrics[n - 1].AdvanceHeight;
        int tsbIndex = glyphId - n;
        if ((uint)tsbIndex >= (uint)_tsbs.Length)
            return false;

        topSideBearing = _tsbs[tsbIndex];
        return true;
    }

    public void SetMetric(ushort glyphId, ushort advanceHeight, short topSideBearing)
    {
        if (glyphId >= _numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        ushort n = _numOfLongVerMetrics;
        if (n == 0 || n > _numGlyphs)
            throw new InvalidOperationException("vmtx builder is in an invalid state (numOfLongVerMetrics).");

        if (glyphId < n)
        {
            _metrics[glyphId] = new LongVerMetricEntry(advanceHeight, topSideBearing);
            MarkDirty();
            return;
        }

        ushort repeated = _metrics[n - 1].AdvanceHeight;
        if (advanceHeight != repeated)
        {
            EnsureFullMetrics();
            _metrics[glyphId] = new LongVerMetricEntry(advanceHeight, topSideBearing);
            MarkDirty();
            return;
        }

        int tsbIndex = glyphId - n;
        if ((uint)tsbIndex >= (uint)_tsbs.Length)
            throw new InvalidOperationException("vmtx builder is in an invalid state (tsb array).");

        _tsbs[tsbIndex] = topSideBearing;
        MarkDirty();
    }

    public void SetAdvanceHeight(ushort glyphId, ushort advanceHeight)
    {
        if (!TryGetMetric(glyphId, out _, out short tsb))
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        SetMetric(glyphId, advanceHeight, tsb);
    }

    public void SetTopSideBearing(ushort glyphId, short topSideBearing)
    {
        if (!TryGetMetric(glyphId, out ushort ah, out _))
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        SetMetric(glyphId, ah, topSideBearing);
    }

    public void EnsureFullMetrics()
    {
        if (_numOfLongVerMetrics == _numGlyphs)
            return;

        ushort oldCount = _numOfLongVerMetrics;
        if (oldCount == 0 || oldCount > _numGlyphs)
            throw new InvalidOperationException("vmtx builder is in an invalid state (numOfLongVerMetrics).");

        var expanded = new LongVerMetricEntry[_numGlyphs];

        int oldCountInt = oldCount;
        _metrics.AsSpan(0, oldCountInt).CopyTo(expanded);

        ushort repeated = _metrics[oldCount - 1].AdvanceHeight;

        for (int i = oldCountInt; i < expanded.Length; i++)
        {
            int tsbIndex = i - oldCountInt;
            if ((uint)tsbIndex >= (uint)_tsbs.Length)
                throw new InvalidOperationException("vmtx builder is in an invalid state (tsb array).");

            expanded[i] = new LongVerMetricEntry(repeated, _tsbs[tsbIndex]);
        }

        _metrics = expanded;
        _tsbs = Array.Empty<short>();
        _numOfLongVerMetrics = _numGlyphs;
        MarkDirty();
    }

    public static bool TryFrom(VmtxTable vmtx, ushort numOfLongVerMetrics, ushort numGlyphs, out VmtxTableBuilder builder)
    {
        builder = null!;

        if (numGlyphs == 0)
            return false;

        if (numOfLongVerMetrics == 0 || numOfLongVerMetrics > numGlyphs)
            return false;

        int requiredLength = GetLength(numGlyphs, numOfLongVerMetrics);
        if (vmtx.Table.Length < requiredLength)
            return false;

        var data = vmtx.Table.Span;
        int fullMetricsBytes = numOfLongVerMetrics * 4;

        var b = new VmtxTableBuilder(numGlyphs, numOfLongVerMetrics);

        int offset = 0;
        for (int i = 0; i < numOfLongVerMetrics; i++)
        {
            b._metrics[i] = new LongVerMetricEntry(
                advanceHeight: BigEndian.ReadUInt16(data, offset),
                topSideBearing: BigEndian.ReadInt16(data, offset + 2));
            offset += 4;
        }

        int tsbCount = numGlyphs - numOfLongVerMetrics;
        for (int i = 0; i < tsbCount; i++)
        {
            b._tsbs[i] = BigEndian.ReadInt16(data, fullMetricsBytes + (i * 2));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_metrics.Length != _numOfLongVerMetrics)
            throw new InvalidOperationException("vmtx builder is in an invalid state (metrics length).");

        if (_tsbs.Length != _numGlyphs - _numOfLongVerMetrics)
            throw new InvalidOperationException("vmtx builder is in an invalid state (tsb length).");

        int length = GetLength(_numGlyphs, _numOfLongVerMetrics);
        byte[] table = new byte[length];
        var span = table.AsSpan();

        int offset = 0;
        for (int i = 0; i < _metrics.Length; i++)
        {
            var m = _metrics[i];
            BigEndian.WriteUInt16(span, offset, m.AdvanceHeight);
            BigEndian.WriteInt16(span, offset + 2, m.TopSideBearing);
            offset += 4;
        }

        for (int i = 0; i < _tsbs.Length; i++)
        {
            BigEndian.WriteInt16(span, offset, _tsbs[i]);
            offset += 2;
        }

        return table;
    }

    private static int GetLength(ushort numGlyphs, ushort numOfLongVerMetrics)
        => checked((numOfLongVerMetrics * 4) + ((numGlyphs - numOfLongVerMetrics) * 2));

    private readonly struct LongVerMetricEntry
    {
        public ushort AdvanceHeight { get; }
        public short TopSideBearing { get; }

        public LongVerMetricEntry(ushort advanceHeight, short topSideBearing)
        {
            AdvanceHeight = advanceHeight;
            TopSideBearing = topSideBearing;
        }
    }
}
