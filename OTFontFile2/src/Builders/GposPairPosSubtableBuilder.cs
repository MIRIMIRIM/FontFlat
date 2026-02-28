namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS PairPos subtables (lookup type 2), format 1 (PairSet).
/// </summary>
public sealed class GposPairPosSubtableBuilder
{
    private readonly List<Pair> _pairs = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int PairCount => _pairs.Count;

    public void Clear()
    {
        if (_pairs.Count == 0)
            return;

        _pairs.Clear();
        MarkDirty();
    }

    public void AddOrReplace(
        ushort firstGlyphId,
        ushort secondGlyphId,
        GposValueRecordBuilder? value1 = null,
        GposValueRecordBuilder? value2 = null)
    {
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            var p = _pairs[i];
            if (p.FirstGlyphId == firstGlyphId && p.SecondGlyphId == secondGlyphId)
                _pairs.RemoveAt(i);
        }

        _pairs.Add(new Pair(firstGlyphId, secondGlyphId, CloneOrEmpty(value1), CloneOrEmpty(value2)));
        MarkDirty();
    }

    public bool Remove(ushort firstGlyphId, ushort secondGlyphId)
    {
        bool removed = false;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            var p = _pairs[i];
            if (p.FirstGlyphId == firstGlyphId && p.SecondGlyphId == secondGlyphId)
            {
                _pairs.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildFormat1Bytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildFormat1Bytes()
    {
        var w = new OTFontFile2.OffsetWriter();
        var devices = new DeviceTablePool();
        var coverageLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(coverageLabel, baseOffset: 0);

        if (_pairs.Count == 0)
        {
            w.WriteUInt16(0);
            w.WriteUInt16(0);
            w.WriteUInt16(0);

            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(new CoverageTableBuilder().ToArray());
            return w.ToArray();
        }

        var pairs = _pairs.ToArray();
        Array.Sort(pairs, static (a, b) =>
        {
            int c = a.FirstGlyphId.CompareTo(b.FirstGlyphId);
            if (c != 0) return c;
            return a.SecondGlyphId.CompareTo(b.SecondGlyphId);
        });

        int uniqueCount = 1;
        for (int i = 1; i < pairs.Length; i++)
        {
            if (pairs[i].FirstGlyphId == pairs[uniqueCount - 1].FirstGlyphId &&
                pairs[i].SecondGlyphId == pairs[uniqueCount - 1].SecondGlyphId)
            {
                pairs[uniqueCount - 1] = pairs[i];
                continue;
            }

            pairs[uniqueCount++] = pairs[i];
        }

        Span<ushort> firstGlyphs = uniqueCount <= 512 ? stackalloc ushort[uniqueCount] : new ushort[uniqueCount];
        Span<int> groupStarts = uniqueCount <= 512 ? stackalloc int[uniqueCount] : new int[uniqueCount];
        Span<int> groupCounts = uniqueCount <= 512 ? stackalloc int[uniqueCount] : new int[uniqueCount];

        ushort valueFormat1 = 0;
        ushort valueFormat2 = 0;

        int groupCount = 0;
        int idx = 0;
        while (idx < uniqueCount)
        {
            ushort first = pairs[idx].FirstGlyphId;
            firstGlyphs[groupCount] = first;
            groupStarts[groupCount] = idx;

            int j = idx;
            while (j < uniqueCount && pairs[j].FirstGlyphId == first)
            {
                valueFormat1 |= pairs[j].Value1.GetValueFormat();
                valueFormat2 |= pairs[j].Value2.GetValueFormat();
                j++;
            }

            groupCounts[groupCount] = j - idx;
            groupCount++;
            idx = j;
        }

        if (groupCount > ushort.MaxValue)
            throw new InvalidOperationException("PairSetCount must fit in uint16.");

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < groupCount; i++)
            coverage.AddGlyph(firstGlyphs[i]);
        byte[] coverageBytes = coverage.ToArray();

        w.WriteUInt16(valueFormat1);
        w.WriteUInt16(valueFormat2);
        w.WriteUInt16(checked((ushort)groupCount));

        Span<OTFontFile2.OffsetWriter.Label> pairSetLabels = groupCount <= 128
            ? stackalloc OTFontFile2.OffsetWriter.Label[groupCount]
            : new OTFontFile2.OffsetWriter.Label[groupCount];

        for (int i = 0; i < groupCount; i++)
        {
            var label = w.CreateLabel();
            pairSetLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        int value1Len = GposValueRecord.GetByteLength(valueFormat1);
        int value2Len = GposValueRecord.GetByteLength(valueFormat2);

        for (int i = 0; i < groupCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(pairSetLabels[i]);

            int start = groupStarts[i];
            int count = groupCounts[i];
            if (count > ushort.MaxValue)
                throw new InvalidOperationException("PairValueCount must fit in uint16.");

            w.WriteUInt16(checked((ushort)count));

            for (int p = 0; p < count; p++)
            {
                var pair = pairs[start + p];
                w.WriteUInt16(pair.SecondGlyphId);

                pair.Value1.WriteTo(w, valueFormat1, posTableBaseOffset: 0, devices);
                pair.Value2.WriteTo(w, valueFormat2, posTableBaseOffset: 0, devices);
            }

        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        devices.EmitAllAligned2(w);
        return w.ToArray();
    }

    private static GposValueRecordBuilder CloneOrEmpty(GposValueRecordBuilder? source)
    {
        var b = new GposValueRecordBuilder();
        if (source is null)
            return b;

        if (source.HasXPlacement) b.XPlacement = source.XPlacement;
        if (source.HasYPlacement) b.YPlacement = source.YPlacement;
        if (source.HasXAdvance) b.XAdvance = source.XAdvance;
        if (source.HasYAdvance) b.YAdvance = source.YAdvance;

        b.XPlacementDevice = source.XPlacementDevice;
        b.YPlacementDevice = source.YPlacementDevice;
        b.XAdvanceDevice = source.XAdvanceDevice;
        b.YAdvanceDevice = source.YAdvanceDevice;

        return b;
    }

    private readonly struct Pair
    {
        public ushort FirstGlyphId { get; }
        public ushort SecondGlyphId { get; }
        public GposValueRecordBuilder Value1 { get; }
        public GposValueRecordBuilder Value2 { get; }

        public Pair(ushort firstGlyphId, ushort secondGlyphId, GposValueRecordBuilder value1, GposValueRecordBuilder value2)
        {
            FirstGlyphId = firstGlyphId;
            SecondGlyphId = secondGlyphId;
            Value1 = value1;
            Value2 = value2;
        }
    }
}
