namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GSUB ReverseChainSingleSubst subtables (lookup type 8), format 1.
/// </summary>
public sealed class GsubReverseChainSingleSubstSubtableBuilder
{
    private readonly List<CoverageTableBuilder> _backtrack = new();
    private readonly List<CoverageTableBuilder> _lookahead = new();
    private readonly List<SubstPair> _pairs = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int BacktrackGlyphCount => _backtrack.Count;
    public int LookaheadGlyphCount => _lookahead.Count;
    public int SubstituteGlyphCount => _pairs.Count;

    public void Clear()
    {
        if (_backtrack.Count == 0 && _lookahead.Count == 0 && _pairs.Count == 0)
            return;

        _backtrack.Clear();
        _lookahead.Clear();
        _pairs.Clear();
        MarkDirty();
    }

    public void ClearBacktrack()
    {
        if (_backtrack.Count == 0)
            return;

        _backtrack.Clear();
        MarkDirty();
    }

    public void ClearLookahead()
    {
        if (_lookahead.Count == 0)
            return;

        _lookahead.Clear();
        MarkDirty();
    }

    public void ClearSubstitutions()
    {
        if (_pairs.Count == 0)
            return;

        _pairs.Clear();
        MarkDirty();
    }

    public void AddBacktrackCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _backtrack.Add(coverage);
        MarkDirty();
    }

    public void AddLookaheadCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _lookahead.Add(coverage);
        MarkDirty();
    }

    public void AddOrReplace(ushort coveredGlyphId, ushort substituteGlyphId)
    {
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].CoveredGlyphId == coveredGlyphId)
                _pairs.RemoveAt(i);
        }

        _pairs.Add(new SubstPair(coveredGlyphId, substituteGlyphId));
        MarkDirty();
    }

    public bool Remove(ushort coveredGlyphId)
    {
        bool removed = false;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].CoveredGlyphId == coveredGlyphId)
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

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        if (_backtrack.Count > ushort.MaxValue) throw new InvalidOperationException("BacktrackGlyphCount must fit in uint16.");
        if (_lookahead.Count > ushort.MaxValue) throw new InvalidOperationException("LookaheadGlyphCount must fit in uint16.");

        int backCount = _backtrack.Count;
        int lookCount = _lookahead.Count;

        var w = new OTFontFile2.OffsetWriter();
        var coverageLabel = w.CreateLabel();
        w.WriteUInt16(1);
        w.WriteOffset16(coverageLabel, baseOffset: 0);

        w.WriteUInt16(checked((ushort)backCount));
        Span<OTFontFile2.OffsetWriter.Label> backLabels = backCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[backCount]
            : new OTFontFile2.OffsetWriter.Label[backCount];
        for (int i = 0; i < backCount; i++)
        {
            var l = w.CreateLabel();
            backLabels[i] = l;
            w.WriteOffset16(l, baseOffset: 0);
        }

        w.WriteUInt16(checked((ushort)lookCount));
        Span<OTFontFile2.OffsetWriter.Label> lookLabels = lookCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[lookCount]
            : new OTFontFile2.OffsetWriter.Label[lookCount];
        for (int i = 0; i < lookCount; i++)
        {
            var l = w.CreateLabel();
            lookLabels[i] = l;
            w.WriteOffset16(l, baseOffset: 0);
        }

        ReadOnlyMemory<byte> coverageBytes;
        if (_pairs.Count == 0)
        {
            w.WriteUInt16(0);
            coverageBytes = new CoverageTableBuilder().ToMemory();
        }
        else
        {
            var pairs = _pairs.ToArray();
            Array.Sort(pairs, static (a, b) => a.CoveredGlyphId.CompareTo(b.CoveredGlyphId));

            int uniqueCount = 1;
            for (int i = 1; i < pairs.Length; i++)
            {
                if (pairs[i].CoveredGlyphId == pairs[uniqueCount - 1].CoveredGlyphId)
                {
                    pairs[uniqueCount - 1] = pairs[i];
                    continue;
                }

                pairs[uniqueCount++] = pairs[i];
            }

            if (uniqueCount > ushort.MaxValue)
                throw new InvalidOperationException("SubstituteGlyphCount must fit in uint16.");

            w.WriteUInt16(checked((ushort)uniqueCount));

            var coverage = new CoverageTableBuilder();
            for (int i = 0; i < uniqueCount; i++)
            {
                coverage.AddGlyph(pairs[i].CoveredGlyphId);
                w.WriteUInt16(pairs[i].SubstituteGlyphId);
            }

            coverageBytes = coverage.ToMemory();
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        for (int i = 0; i < backCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(backLabels[i]);
            w.WriteBytes(_backtrack[i].ToMemory());
        }

        for (int i = 0; i < lookCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(lookLabels[i]);
            w.WriteBytes(_lookahead[i].ToMemory());
        }

        return w.ToArray();
    }

    private readonly struct SubstPair
    {
        public ushort CoveredGlyphId { get; }
        public ushort SubstituteGlyphId { get; }

        public SubstPair(ushort coveredGlyphId, ushort substituteGlyphId)
        {
            CoveredGlyphId = coveredGlyphId;
            SubstituteGlyphId = substituteGlyphId;
        }
    }
}
