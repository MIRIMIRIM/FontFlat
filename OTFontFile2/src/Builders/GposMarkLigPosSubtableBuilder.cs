namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS MarkLigPos subtables (lookup type 5), format 1.
/// </summary>
public sealed class GposMarkLigPosSubtableBuilder
{
    private readonly List<MarkEntry> _marks = new();
    private readonly List<LigatureAnchorEntry> _ligAnchors = new();

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
        _marks.Clear();
        _ligAnchors.Clear();
        _hasClassCountOverride = false;
        _classCountOverride = 1;
        MarkDirty();
    }

    public void AddOrReplaceMark(ushort markGlyphId, ushort @class, AnchorTableBuilder markAnchor)
    {
        if (markAnchor is null) throw new ArgumentNullException(nameof(markAnchor));

        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            if (_marks[i].GlyphId == markGlyphId)
                _marks.RemoveAt(i);
        }

        _marks.Add(new MarkEntry(markGlyphId, @class, markAnchor));
        MarkDirty();
    }

    public bool RemoveMark(ushort markGlyphId)
    {
        bool removed = false;
        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            if (_marks[i].GlyphId == markGlyphId)
            {
                _marks.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public void AddOrReplaceLigatureAnchor(ushort ligatureGlyphId, ushort componentIndex, ushort @class, AnchorTableBuilder ligatureAnchor)
    {
        if (ligatureAnchor is null) throw new ArgumentNullException(nameof(ligatureAnchor));

        for (int i = _ligAnchors.Count - 1; i >= 0; i--)
        {
            var e = _ligAnchors[i];
            if (e.GlyphId == ligatureGlyphId && e.ComponentIndex == componentIndex && e.Class == @class)
                _ligAnchors.RemoveAt(i);
        }

        _ligAnchors.Add(new LigatureAnchorEntry(ligatureGlyphId, componentIndex, @class, ligatureAnchor));
        MarkDirty();
    }

    public bool RemoveLigatureAnchor(ushort ligatureGlyphId, ushort componentIndex, ushort @class)
    {
        bool removed = false;
        for (int i = _ligAnchors.Count - 1; i >= 0; i--)
        {
            var e = _ligAnchors[i];
            if (e.GlyphId == ligatureGlyphId && e.ComponentIndex == componentIndex && e.Class == @class)
            {
                _ligAnchors.RemoveAt(i);
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

        var markCoverageLabel = w.CreateLabel();
        var ligCoverageLabel = w.CreateLabel();
        var markArrayLabel = w.CreateLabel();
        var ligArrayLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(markCoverageLabel, baseOffset: 0);
        w.WriteOffset16(ligCoverageLabel, baseOffset: 0);
        w.WriteUInt16(classCount);
        w.WriteOffset16(markArrayLabel, baseOffset: 0);
        w.WriteOffset16(ligArrayLabel, baseOffset: 0);

        var marks = _marks.Count == 0 ? Array.Empty<MarkEntry>() : _marks.ToArray();
        if (marks.Length != 0)
        {
            Array.Sort(marks, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
            int unique = 1;
            for (int i = 1; i < marks.Length; i++)
            {
                if (marks[i].GlyphId == marks[unique - 1].GlyphId)
                {
                    marks[unique - 1] = marks[i];
                    continue;
                }

                marks[unique++] = marks[i];
            }

            if (unique != marks.Length)
                Array.Resize(ref marks, unique);
        }

        if (marks.Length > ushort.MaxValue)
            throw new InvalidOperationException("MarkCount must fit in uint16.");

        var ligAnchors = _ligAnchors.Count == 0 ? Array.Empty<LigatureAnchorEntry>() : _ligAnchors.ToArray();
        if (ligAnchors.Length != 0)
        {
            Array.Sort(ligAnchors, static (a, b) =>
            {
                int c = a.GlyphId.CompareTo(b.GlyphId);
                if (c != 0) return c;
                c = a.ComponentIndex.CompareTo(b.ComponentIndex);
                if (c != 0) return c;
                return a.Class.CompareTo(b.Class);
            });

            int unique = 1;
            for (int i = 1; i < ligAnchors.Length; i++)
            {
                var prev = ligAnchors[unique - 1];
                var cur = ligAnchors[i];
                if (cur.GlyphId == prev.GlyphId && cur.ComponentIndex == prev.ComponentIndex && cur.Class == prev.Class)
                {
                    ligAnchors[unique - 1] = cur;
                    continue;
                }

                ligAnchors[unique++] = cur;
            }

            if (unique != ligAnchors.Length)
                Array.Resize(ref ligAnchors, unique);
        }

        // Unique ligature glyph ids + per-ligature component count.
        Span<ushort> ligGlyphs = ligAnchors.Length == 0 ? Span<ushort>.Empty
            : ligAnchors.Length <= 512 ? stackalloc ushort[ligAnchors.Length] : new ushort[ligAnchors.Length];

        var componentCountByLig = new Dictionary<ushort, ushort>(ligAnchors.Length);

        int ligGlyphCount = 0;
        ushort lastLig = 0;
        for (int i = 0; i < ligAnchors.Length; i++)
        {
            var e = ligAnchors[i];
            ushort gid = e.GlyphId;
            if (ligGlyphCount == 0 || gid != lastLig)
            {
                ligGlyphs[ligGlyphCount++] = gid;
                lastLig = gid;
            }

            ushort compCount = checked((ushort)(e.ComponentIndex + 1));
            if (componentCountByLig.TryGetValue(gid, out ushort existing))
            {
                if (compCount > existing)
                    componentCountByLig[gid] = compCount;
            }
            else
            {
                componentCountByLig.Add(gid, compCount);
            }
        }

        if (ligGlyphCount > ushort.MaxValue)
            throw new InvalidOperationException("LigatureCount must fit in uint16.");

        // Mark coverage.
        var markCoverage = new CoverageTableBuilder();
        for (int i = 0; i < marks.Length; i++)
        {
            if (marks[i].Class >= classCount)
                throw new InvalidOperationException("MarkRecord class must be < ClassCount.");

            markCoverage.AddGlyph(marks[i].GlyphId);
        }
        byte[] markCoverageBytes = markCoverage.ToArray();

        // Ligature coverage.
        var ligCoverage = new CoverageTableBuilder();
        ligCoverage.AddGlyphs(ligGlyphs.Slice(0, ligGlyphCount));
        byte[] ligCoverageBytes = ligCoverage.ToArray();

        // Ligature anchors lookup.
        var ligAnchorByKey = new Dictionary<ulong, AnchorTableBuilder>(ligAnchors.Length);
        for (int i = 0; i < ligAnchors.Length; i++)
        {
            if (ligAnchors[i].Class >= classCount)
                throw new InvalidOperationException("LigatureAnchor class must be < ClassCount.");

            ligAnchorByKey[LigKey(ligAnchors[i].GlyphId, ligAnchors[i].ComponentIndex, ligAnchors[i].Class)] = ligAnchors[i].Anchor;
        }

        w.Align2();
        w.DefineLabelHere(markCoverageLabel);
        w.WriteBytes(markCoverageBytes);

        w.Align2();
        w.DefineLabelHere(ligCoverageLabel);
        w.WriteBytes(ligCoverageBytes);

        w.Align2();
        w.DefineLabelHere(markArrayLabel);
        int markArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)marks.Length));
        for (int i = 0; i < marks.Length; i++)
        {
            w.WriteUInt16(marks[i].Class);
            anchors.WriteOffset16(w, marks[i].Anchor, baseOffset: markArrayStart);
        }

        // LigatureArray.
        w.Align2();
        w.DefineLabelHere(ligArrayLabel);
        int ligArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)ligGlyphCount));

        Span<OTFontFile2.OffsetWriter.Label> attachLabels = ligGlyphCount <= 128
            ? stackalloc OTFontFile2.OffsetWriter.Label[ligGlyphCount]
            : new OTFontFile2.OffsetWriter.Label[ligGlyphCount];

        for (int i = 0; i < ligGlyphCount; i++)
        {
            var label = w.CreateLabel();
            attachLabels[i] = label;
            w.WriteOffset16(label, baseOffset: ligArrayStart);
        }

        for (int i = 0; i < ligGlyphCount; i++)
        {
            ushort ligGlyphId = ligGlyphs[i];
            if (!componentCountByLig.TryGetValue(ligGlyphId, out ushort componentCount))
                componentCount = 0;

            w.Align2();
            w.DefineLabelHere(attachLabels[i]);
            int attachStart = w.Position;

            w.WriteUInt16(componentCount);
            for (ushort r = 0; r < componentCount; r++)
            {
                for (ushort c = 0; c < classCount; c++)
                {
                    if (ligAnchorByKey.TryGetValue(LigKey(ligGlyphId, r, c), out var anchor))
                        anchors.WriteOffset16(w, anchor, baseOffset: attachStart);
                    else
                        w.WriteUInt16(0);
                }
            }
        }

        anchors.EmitAllAligned2(w, devices);
        devices.EmitAllAligned2(w);

        return w.ToArray();
    }

    private ushort ComputeRequiredClassCount()
    {
        ushort max = 0;
        for (int i = 0; i < _marks.Count; i++)
        {
            ushort c = _marks[i].Class;
            if (c > max) max = c;
        }

        for (int i = 0; i < _ligAnchors.Count; i++)
        {
            ushort c = _ligAnchors[i].Class;
            if (c > max) max = c;
        }

        return checked((ushort)(max + 1));
    }

    private static ulong LigKey(ushort glyphId, ushort componentIndex, ushort @class)
        => ((ulong)glyphId << 32) | ((ulong)componentIndex << 16) | @class;

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

    private readonly struct LigatureAnchorEntry
    {
        public ushort GlyphId { get; }
        public ushort ComponentIndex { get; }
        public ushort Class { get; }
        public AnchorTableBuilder Anchor { get; }

        public LigatureAnchorEntry(ushort glyphId, ushort componentIndex, ushort @class, AnchorTableBuilder anchor)
        {
            GlyphId = glyphId;
            ComponentIndex = componentIndex;
            Class = @class;
            Anchor = anchor;
        }
    }
}

