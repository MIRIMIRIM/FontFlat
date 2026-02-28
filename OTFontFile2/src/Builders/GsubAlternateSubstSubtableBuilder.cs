namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GSUB AlternateSubst subtables (lookup type 3), format 1.
/// </summary>
public sealed class GsubAlternateSubstSubtableBuilder
{
    private readonly List<Entry> _entries = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int EntryCount => _entries.Count;

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort fromGlyphId, ReadOnlySpan<ushort> alternates)
    {
        if (alternates.Length == 0)
            throw new ArgumentException("AlternateSubst alternates must be non-empty.", nameof(alternates));

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].FromGlyphId == fromGlyphId)
                _entries.RemoveAt(i);
        }

        _entries.Add(new Entry(fromGlyphId, alternates.ToArray()));
        MarkDirty();
    }

    public bool Remove(ushort fromGlyphId)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].FromGlyphId == fromGlyphId)
            {
                _entries.RemoveAt(i);
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
        var w = new OTFontFile2.OffsetWriter();
        var coverageLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(coverageLabel, baseOffset: 0);

        if (_entries.Count == 0)
        {
            w.WriteUInt16(0);
            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(new CoverageTableBuilder().ToArray());
            return w.ToArray();
        }

        var entries = _entries.ToArray();
        Array.Sort(entries, static (a, b) => a.FromGlyphId.CompareTo(b.FromGlyphId));

        int uniqueCount = 1;
        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].FromGlyphId == entries[uniqueCount - 1].FromGlyphId)
            {
                entries[uniqueCount - 1] = entries[i];
                continue;
            }

            entries[uniqueCount++] = entries[i];
        }

        if (uniqueCount != entries.Length)
            Array.Resize(ref entries, uniqueCount);

        if (uniqueCount > ushort.MaxValue)
            throw new InvalidOperationException("AlternateSetCount must fit in uint16.");

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < uniqueCount; i++)
            coverage.AddGlyph(entries[i].FromGlyphId);
        byte[] coverageBytes = coverage.ToArray();

        w.WriteUInt16(checked((ushort)uniqueCount));

        Span<OTFontFile2.OffsetWriter.Label> setLabels = uniqueCount <= 128
            ? stackalloc OTFontFile2.OffsetWriter.Label[uniqueCount]
            : new OTFontFile2.OffsetWriter.Label[uniqueCount];

        for (int i = 0; i < uniqueCount; i++)
        {
            var label = w.CreateLabel();
            setLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        for (int i = 0; i < uniqueCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(setLabels[i]);

            var alts = entries[i].Alternates;
            if (alts.Length == 0)
                throw new InvalidOperationException("AlternateSet glyphCount must be >= 1.");
            if (alts.Length > ushort.MaxValue)
                throw new InvalidOperationException("AlternateSet glyphCount must fit in uint16.");

            w.WriteUInt16(checked((ushort)alts.Length));
            for (int a = 0; a < alts.Length; a++)
                w.WriteUInt16(alts[a]);
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        return w.ToArray();
    }

    private readonly struct Entry
    {
        public ushort FromGlyphId { get; }
        public ushort[] Alternates { get; }

        public Entry(ushort fromGlyphId, ushort[] alternates)
        {
            FromGlyphId = fromGlyphId;
            Alternates = alternates;
        }
    }
}

