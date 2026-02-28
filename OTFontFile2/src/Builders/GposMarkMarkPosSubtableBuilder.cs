namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS MarkMarkPos subtables (lookup type 6), format 1.
/// </summary>
public sealed class GposMarkMarkPosSubtableBuilder
{
    private readonly List<MarkEntry> _mark1 = new();
    private readonly List<Mark2AnchorEntry> _mark2Anchors = new();

    private bool _hasClassCountOverride;
    private ushort _classCountOverride = 1;

    private bool _dirty = true;
    private byte[]? _built;

    public ushort ClassCount
    {
        get => _hasClassCountOverride ? _classCountOverride : ComputeRequiredClassCount();
        set
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_hasClassCountOverride && value == _classCountOverride)
                return;

            _hasClassCountOverride = true;
            _classCountOverride = value;
            MarkDirty();
        }
    }

    public void ClearClassCountOverride()
    {
        if (!_hasClassCountOverride)
            return;

        _hasClassCountOverride = false;
        MarkDirty();
    }

    public void Clear()
    {
        _mark1.Clear();
        _mark2Anchors.Clear();
        _hasClassCountOverride = false;
        _classCountOverride = 1;
        MarkDirty();
    }

    public void AddOrReplaceMark1(ushort markGlyphId, ushort @class, AnchorTableBuilder markAnchor)
    {
        if (markAnchor is null) throw new ArgumentNullException(nameof(markAnchor));

        for (int i = _mark1.Count - 1; i >= 0; i--)
        {
            if (_mark1[i].GlyphId == markGlyphId)
                _mark1.RemoveAt(i);
        }

        _mark1.Add(new MarkEntry(markGlyphId, @class, markAnchor));
        MarkDirty();
    }

    public bool RemoveMark1(ushort markGlyphId)
    {
        bool removed = false;
        for (int i = _mark1.Count - 1; i >= 0; i--)
        {
            if (_mark1[i].GlyphId == markGlyphId)
            {
                _mark1.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public void AddOrReplaceMark2Anchor(ushort mark2GlyphId, ushort @class, AnchorTableBuilder mark2Anchor)
    {
        if (mark2Anchor is null) throw new ArgumentNullException(nameof(mark2Anchor));

        for (int i = _mark2Anchors.Count - 1; i >= 0; i--)
        {
            var e = _mark2Anchors[i];
            if (e.GlyphId == mark2GlyphId && e.Class == @class)
                _mark2Anchors.RemoveAt(i);
        }

        _mark2Anchors.Add(new Mark2AnchorEntry(mark2GlyphId, @class, mark2Anchor));
        MarkDirty();
    }

    public bool RemoveMark2Anchor(ushort mark2GlyphId, ushort @class)
    {
        bool removed = false;
        for (int i = _mark2Anchors.Count - 1; i >= 0; i--)
        {
            var e = _mark2Anchors[i];
            if (e.GlyphId == mark2GlyphId && e.Class == @class)
            {
                _mark2Anchors.RemoveAt(i);
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
        ushort classCount = ClassCount;
        if (classCount == 0)
            throw new InvalidOperationException("ClassCount must be >= 1.");

        var w = new OTFontFile2.OffsetWriter();
        var devices = new DeviceTablePool();
        var anchors = new AnchorTablePool();

        var mark1CoverageLabel = w.CreateLabel();
        var mark2CoverageLabel = w.CreateLabel();
        var mark1ArrayLabel = w.CreateLabel();
        var mark2ArrayLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(mark1CoverageLabel, baseOffset: 0);
        w.WriteOffset16(mark2CoverageLabel, baseOffset: 0);
        w.WriteUInt16(classCount);
        w.WriteOffset16(mark1ArrayLabel, baseOffset: 0);
        w.WriteOffset16(mark2ArrayLabel, baseOffset: 0);

        var mark1 = _mark1.Count == 0 ? Array.Empty<MarkEntry>() : _mark1.ToArray();
        if (mark1.Length != 0)
        {
            Array.Sort(mark1, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
            int unique = 1;
            for (int i = 1; i < mark1.Length; i++)
            {
                if (mark1[i].GlyphId == mark1[unique - 1].GlyphId)
                {
                    mark1[unique - 1] = mark1[i];
                    continue;
                }

                mark1[unique++] = mark1[i];
            }

            if (unique != mark1.Length)
                Array.Resize(ref mark1, unique);
        }

        if (mark1.Length > ushort.MaxValue)
            throw new InvalidOperationException("Mark1Count must fit in uint16.");

        var mark2 = _mark2Anchors.Count == 0 ? Array.Empty<Mark2AnchorEntry>() : _mark2Anchors.ToArray();
        if (mark2.Length != 0)
        {
            Array.Sort(mark2, static (a, b) =>
            {
                int c = a.GlyphId.CompareTo(b.GlyphId);
                if (c != 0) return c;
                return a.Class.CompareTo(b.Class);
            });

            int unique = 1;
            for (int i = 1; i < mark2.Length; i++)
            {
                if (mark2[i].GlyphId == mark2[unique - 1].GlyphId && mark2[i].Class == mark2[unique - 1].Class)
                {
                    mark2[unique - 1] = mark2[i];
                    continue;
                }

                mark2[unique++] = mark2[i];
            }

            if (unique != mark2.Length)
                Array.Resize(ref mark2, unique);
        }

        // Unique mark2 glyph ids.
        Span<ushort> mark2Glyphs = mark2.Length == 0 ? Span<ushort>.Empty
            : mark2.Length <= 512 ? stackalloc ushort[mark2.Length] : new ushort[mark2.Length];

        int mark2GlyphCount = 0;
        ushort last = 0;
        for (int i = 0; i < mark2.Length; i++)
        {
            ushort gid = mark2[i].GlyphId;
            if (mark2GlyphCount == 0 || gid != last)
            {
                mark2Glyphs[mark2GlyphCount++] = gid;
                last = gid;
            }
        }

        if (mark2GlyphCount > ushort.MaxValue)
            throw new InvalidOperationException("Mark2Count must fit in uint16.");

        var mark1Coverage = new CoverageTableBuilder();
        for (int i = 0; i < mark1.Length; i++)
        {
            if (mark1[i].Class >= classCount)
                throw new InvalidOperationException("Mark1Record class must be < ClassCount.");

            mark1Coverage.AddGlyph(mark1[i].GlyphId);
        }
        byte[] mark1CoverageBytes = mark1Coverage.ToArray();

        var mark2Coverage = new CoverageTableBuilder();
        mark2Coverage.AddGlyphs(mark2Glyphs.Slice(0, mark2GlyphCount));
        byte[] mark2CoverageBytes = mark2Coverage.ToArray();

        // mark2 anchor lookup.
        var mark2AnchorByKey = new Dictionary<uint, AnchorTableBuilder>(mark2.Length);
        for (int i = 0; i < mark2.Length; i++)
        {
            if (mark2[i].Class >= classCount)
                throw new InvalidOperationException("Mark2Anchor class must be < ClassCount.");

            mark2AnchorByKey[Key(mark2[i].GlyphId, mark2[i].Class)] = mark2[i].Anchor;
        }

        w.Align2();
        w.DefineLabelHere(mark1CoverageLabel);
        w.WriteBytes(mark1CoverageBytes);

        w.Align2();
        w.DefineLabelHere(mark2CoverageLabel);
        w.WriteBytes(mark2CoverageBytes);

        w.Align2();
        w.DefineLabelHere(mark1ArrayLabel);
        int mark1ArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)mark1.Length));
        for (int i = 0; i < mark1.Length; i++)
        {
            w.WriteUInt16(mark1[i].Class);
            anchors.WriteOffset16(w, mark1[i].Anchor, baseOffset: mark1ArrayStart);
        }

        w.Align2();
        w.DefineLabelHere(mark2ArrayLabel);
        int mark2ArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)mark2GlyphCount));

        for (int r = 0; r < mark2GlyphCount; r++)
        {
            ushort glyphId = mark2Glyphs[r];
            for (ushort c = 0; c < classCount; c++)
            {
                if (mark2AnchorByKey.TryGetValue(Key(glyphId, c), out var anchor))
                    anchors.WriteOffset16(w, anchor, baseOffset: mark2ArrayStart);
                else
                    w.WriteUInt16(0);
            }
        }

        anchors.EmitAllAligned2(w, devices);
        devices.EmitAllAligned2(w);

        return w.ToArray();
    }

    private ushort ComputeRequiredClassCount()
    {
        ushort max = 0;
        for (int i = 0; i < _mark1.Count; i++)
        {
            ushort c = _mark1[i].Class;
            if (c > max) max = c;
        }

        for (int i = 0; i < _mark2Anchors.Count; i++)
        {
            ushort c = _mark2Anchors[i].Class;
            if (c > max) max = c;
        }

        return checked((ushort)(max + 1));
    }

    private static uint Key(ushort glyphId, ushort @class) => ((uint)glyphId << 16) | @class;

    private readonly struct MarkEntry
    {
        public ushort GlyphId { get; }
        public ushort Class { get; }
        public AnchorTableBuilder Anchor { get; }

        public MarkEntry(ushort glyphId, ushort @class, AnchorTableBuilder anchor)
        {
            GlyphId = glyphId;
            Class = @class;
            Anchor = anchor;
        }
    }

    private readonly struct Mark2AnchorEntry
    {
        public ushort GlyphId { get; }
        public ushort Class { get; }
        public AnchorTableBuilder Anchor { get; }

        public Mark2AnchorEntry(ushort glyphId, ushort @class, AnchorTableBuilder anchor)
        {
            GlyphId = glyphId;
            Class = @class;
            Anchor = anchor;
        }
    }
}

