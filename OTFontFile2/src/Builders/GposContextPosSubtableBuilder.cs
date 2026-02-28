namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ContextPos subtables (lookup type 7).
/// </summary>
/// <remarks>
/// Currently supports format 3 (coverage-based) only.
/// </remarks>
public sealed class GposContextPosSubtableBuilder
{
    private readonly List<CoverageTableBuilder> _coverages = new();
    private readonly List<SequenceLookupRecord> _records = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int GlyphCount => _coverages.Count;
    public int PosCount => _records.Count;

    public void Clear()
    {
        if (_coverages.Count == 0 && _records.Count == 0)
            return;

        _coverages.Clear();
        _records.Clear();
        MarkDirty();
    }

    public void ClearCoverages()
    {
        if (_coverages.Count == 0)
            return;

        _coverages.Clear();
        MarkDirty();
    }

    public void ClearPosLookupRecords()
    {
        if (_records.Count == 0)
            return;

        _records.Clear();
        MarkDirty();
    }

    public void AddCoverage(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        _coverages.Add(coverage);
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
        if (_coverages.Count > ushort.MaxValue)
            throw new InvalidOperationException("GlyphCount must fit in uint16.");
        if (_records.Count > ushort.MaxValue)
            throw new InvalidOperationException("PosCount must fit in uint16.");

        int glyphCount = _coverages.Count;
        int posCount = _records.Count;

        var w = new OTFontFile2.OffsetWriter();
        w.WriteUInt16(3);
        w.WriteUInt16(checked((ushort)glyphCount));
        w.WriteUInt16(checked((ushort)posCount));

        Span<OTFontFile2.OffsetWriter.Label> coverageLabels = glyphCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[glyphCount]
            : new OTFontFile2.OffsetWriter.Label[glyphCount];

        for (int i = 0; i < glyphCount; i++)
        {
            var label = w.CreateLabel();
            coverageLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        for (int i = 0; i < posCount; i++)
        {
            var r = _records[i];
            w.WriteUInt16(r.SequenceIndex);
            w.WriteUInt16(r.LookupListIndex);
        }

        for (int i = 0; i < glyphCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(coverageLabels[i]);
            w.WriteBytes(_coverages[i].ToMemory());
        }

        return w.ToArray();
    }
}

