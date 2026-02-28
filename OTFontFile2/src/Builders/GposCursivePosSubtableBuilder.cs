namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS CursivePos subtables (lookup type 3), format 1.
/// </summary>
public sealed class GposCursivePosSubtableBuilder
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

    public void AddOrReplace(ushort glyphId, AnchorTableBuilder? entryAnchor, AnchorTableBuilder? exitAnchor)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].GlyphId == glyphId)
                _entries.RemoveAt(i);
        }

        _entries.Add(new Entry(glyphId, entryAnchor, exitAnchor));
        MarkDirty();
    }

    public bool Remove(ushort glyphId)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].GlyphId == glyphId)
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
        var devices = new DeviceTablePool();
        var anchors = new AnchorTablePool();

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
        Array.Sort(entries, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));

        int uniqueCount = 1;
        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].GlyphId == entries[uniqueCount - 1].GlyphId)
            {
                entries[uniqueCount - 1] = entries[i];
                continue;
            }

            entries[uniqueCount++] = entries[i];
        }

        if (uniqueCount > ushort.MaxValue)
            throw new InvalidOperationException("EntryExitCount must fit in uint16.");

        Span<ushort> glyphs = uniqueCount <= 256 ? stackalloc ushort[uniqueCount] : new ushort[uniqueCount];
        for (int i = 0; i < uniqueCount; i++)
            glyphs[i] = entries[i].GlyphId;

        var coverage = new CoverageTableBuilder();
        coverage.AddGlyphs(glyphs.Slice(0, uniqueCount));
        byte[] coverageBytes = coverage.ToArray();

        w.WriteUInt16(checked((ushort)uniqueCount));

        for (int i = 0; i < uniqueCount; i++)
        {
            var e = entries[i];

            if (e.EntryAnchor is null) w.WriteUInt16(0);
            else anchors.WriteOffset16(w, e.EntryAnchor, baseOffset: 0);

            if (e.ExitAnchor is null) w.WriteUInt16(0);
            else anchors.WriteOffset16(w, e.ExitAnchor, baseOffset: 0);
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        anchors.EmitAllAligned2(w, devices);
        devices.EmitAllAligned2(w);

        return w.ToArray();
    }

    private readonly struct Entry
    {
        public ushort GlyphId { get; }
        public AnchorTableBuilder? EntryAnchor { get; }
        public AnchorTableBuilder? ExitAnchor { get; }

        public Entry(ushort glyphId, AnchorTableBuilder? entryAnchor, AnchorTableBuilder? exitAnchor)
        {
            GlyphId = glyphId;
            EntryAnchor = entryAnchor;
            ExitAnchor = exitAnchor;
        }
    }
}

