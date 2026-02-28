namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ChainContextPos subtables (lookup type 8).
/// </summary>
/// <remarks>
/// Currently supports format 3 (coverage-based) only.
/// </remarks>
public sealed class GposChainContextPosSubtableBuilder
{
    private readonly List<CoverageTableBuilder> _backtrack = new();
    private readonly List<CoverageTableBuilder> _input = new();
    private readonly List<CoverageTableBuilder> _lookahead = new();
    private readonly List<SequenceLookupRecord> _records = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int BacktrackGlyphCount => _backtrack.Count;
    public int InputGlyphCount => _input.Count;
    public int LookaheadGlyphCount => _lookahead.Count;
    public int PosCount => _records.Count;

    public void Clear()
    {
        if (_backtrack.Count == 0 && _input.Count == 0 && _lookahead.Count == 0 && _records.Count == 0)
            return;

        _backtrack.Clear();
        _input.Clear();
        _lookahead.Clear();
        _records.Clear();
        MarkDirty();
    }

    public void ClearBacktrack()
    {
        if (_backtrack.Count == 0)
            return;

        _backtrack.Clear();
        MarkDirty();
    }

    public void ClearInput()
    {
        if (_input.Count == 0)
            return;

        _input.Clear();
        MarkDirty();
    }

    public void ClearLookahead()
    {
        if (_lookahead.Count == 0)
            return;

        _lookahead.Clear();
        MarkDirty();
    }

    public void ClearPosLookupRecords()
    {
        if (_records.Count == 0)
            return;

        _records.Clear();
        MarkDirty();
    }

    public void AddBacktrackCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _backtrack.Add(coverage);
        MarkDirty();
    }

    public void AddInputCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _input.Add(coverage);
        MarkDirty();
    }

    public void AddLookaheadCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _lookahead.Add(coverage);
        MarkDirty();
    }

    public void AddPosLookupRecord(ushort sequenceIndex, ushort lookupListIndex)
    {
        _records.Add(new SequenceLookupRecord(sequenceIndex, lookupListIndex));
        MarkDirty();
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

        _built = BuildFormat3Bytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildFormat3Bytes()
    {
        if (_backtrack.Count > ushort.MaxValue) throw new InvalidOperationException("BacktrackGlyphCount must fit in uint16.");
        if (_input.Count > ushort.MaxValue) throw new InvalidOperationException("InputGlyphCount must fit in uint16.");
        if (_lookahead.Count > ushort.MaxValue) throw new InvalidOperationException("LookaheadGlyphCount must fit in uint16.");
        if (_records.Count > ushort.MaxValue) throw new InvalidOperationException("PosCount must fit in uint16.");

        int backCount = _backtrack.Count;
        int inputCount = _input.Count;
        int lookCount = _lookahead.Count;
        int posCount = _records.Count;

        var w = new OTFontFile2.OffsetWriter();
        w.WriteUInt16(3);

        w.WriteUInt16(checked((ushort)backCount));
        Span<OTFontFile2.OffsetWriter.Label> backLabels = backCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[backCount]
            : new OTFontFile2.OffsetWriter.Label[backCount];

        for (int i = 0; i < backCount; i++)
        {
            var label = w.CreateLabel();
            backLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        w.WriteUInt16(checked((ushort)inputCount));
        Span<OTFontFile2.OffsetWriter.Label> inputLabels = inputCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[inputCount]
            : new OTFontFile2.OffsetWriter.Label[inputCount];

        for (int i = 0; i < inputCount; i++)
        {
            var label = w.CreateLabel();
            inputLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        w.WriteUInt16(checked((ushort)lookCount));
        Span<OTFontFile2.OffsetWriter.Label> lookLabels = lookCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[lookCount]
            : new OTFontFile2.OffsetWriter.Label[lookCount];

        for (int i = 0; i < lookCount; i++)
        {
            var label = w.CreateLabel();
            lookLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        w.WriteUInt16(checked((ushort)posCount));
        for (int i = 0; i < posCount; i++)
        {
            var r = _records[i];
            w.WriteUInt16(r.SequenceIndex);
            w.WriteUInt16(r.LookupListIndex);
        }

        for (int i = 0; i < backCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(backLabels[i]);
            w.WriteBytes(_backtrack[i].ToMemory());
        }

        for (int i = 0; i < inputCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(inputLabels[i]);
            w.WriteBytes(_input[i].ToMemory());
        }

        for (int i = 0; i < lookCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(lookLabels[i]);
            w.WriteBytes(_lookahead[i].ToMemory());
        }

        return w.ToArray();
    }
}

