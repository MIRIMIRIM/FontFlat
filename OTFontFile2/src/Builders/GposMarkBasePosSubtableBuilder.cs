namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS MarkBasePos subtables (lookup type 4), format 1.
/// </summary>
public sealed class GposMarkBasePosSubtableBuilder
{
    private readonly List<MarkEntry> _marks = new();
    private readonly List<BaseAnchorEntry> _baseAnchors = new();

    private bool _hasClassCountOverride;
    private ushort _classCountOverride = 1;

    private bool _dirty = true;
    private byte[]? _built;

    public int MarkCount => _marks.Count;
    public int BaseAnchorCount => _baseAnchors.Count;

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
        _baseAnchors.Clear();
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

    public void AddOrReplaceBaseAnchor(ushort baseGlyphId, ushort @class, AnchorTableBuilder baseAnchor)
    {
        if (baseAnchor is null) throw new ArgumentNullException(nameof(baseAnchor));

        for (int i = _baseAnchors.Count - 1; i >= 0; i--)
        {
            var e = _baseAnchors[i];
            if (e.GlyphId == baseGlyphId && e.Class == @class)
                _baseAnchors.RemoveAt(i);
        }

        _baseAnchors.Add(new BaseAnchorEntry(baseGlyphId, @class, baseAnchor));
        MarkDirty();
    }

    public bool RemoveBaseAnchor(ushort baseGlyphId, ushort @class)
    {
        bool removed = false;
        for (int i = _baseAnchors.Count - 1; i >= 0; i--)
        {
            var e = _baseAnchors[i];
            if (e.GlyphId == baseGlyphId && e.Class == @class)
            {
                _baseAnchors.RemoveAt(i);
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
        var baseCoverageLabel = w.CreateLabel();
        var markArrayLabel = w.CreateLabel();
        var baseArrayLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(markCoverageLabel, baseOffset: 0);
        w.WriteOffset16(baseCoverageLabel, baseOffset: 0);
        w.WriteUInt16(classCount);
        w.WriteOffset16(markArrayLabel, baseOffset: 0);
        w.WriteOffset16(baseArrayLabel, baseOffset: 0);

        // Marks.
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

        // Bases.
        var bases = _baseAnchors.Count == 0 ? Array.Empty<BaseAnchorEntry>() : _baseAnchors.ToArray();
        if (bases.Length != 0)
        {
            Array.Sort(bases, static (a, b) =>
            {
                int c = a.GlyphId.CompareTo(b.GlyphId);
                if (c != 0) return c;
                return a.Class.CompareTo(b.Class);
            });

            int unique = 1;
            for (int i = 1; i < bases.Length; i++)
            {
                if (bases[i].GlyphId == bases[unique - 1].GlyphId && bases[i].Class == bases[unique - 1].Class)
                {
                    bases[unique - 1] = bases[i];
                    continue;
                }

                bases[unique++] = bases[i];
            }

            if (unique != bases.Length)
                Array.Resize(ref bases, unique);
        }

        // Unique base glyph ids.
        Span<ushort> baseGlyphs = bases.Length == 0 ? Span<ushort>.Empty
            : bases.Length <= 512 ? stackalloc ushort[bases.Length] : new ushort[bases.Length];

        int baseGlyphCount = 0;
        ushort last = 0;
        for (int i = 0; i < bases.Length; i++)
        {
            ushort gid = bases[i].GlyphId;
            if (baseGlyphCount == 0 || gid != last)
            {
                baseGlyphs[baseGlyphCount++] = gid;
                last = gid;
            }
        }

        if (baseGlyphCount > ushort.MaxValue)
            throw new InvalidOperationException("BaseCount must fit in uint16.");

        // Build coverage tables.
        var markCoverage = new CoverageTableBuilder();
        for (int i = 0; i < marks.Length; i++)
        {
            if (marks[i].Class >= classCount)
                throw new InvalidOperationException("MarkRecord class must be < ClassCount.");

            markCoverage.AddGlyph(marks[i].GlyphId);
        }
        byte[] markCoverageBytes = markCoverage.ToArray();

        var baseCoverage = new CoverageTableBuilder();
        baseCoverage.AddGlyphs(baseGlyphs.Slice(0, baseGlyphCount));
        byte[] baseCoverageBytes = baseCoverage.ToArray();

        // Base anchor lookup.
        var baseAnchorByKey = new Dictionary<uint, AnchorTableBuilder>(bases.Length);
        for (int i = 0; i < bases.Length; i++)
        {
            if (bases[i].Class >= classCount)
                throw new InvalidOperationException("BaseAnchor class must be < ClassCount.");

            baseAnchorByKey[Key(bases[i].GlyphId, bases[i].Class)] = bases[i].Anchor;
        }

        // Write coverages.
        w.Align2();
        w.DefineLabelHere(markCoverageLabel);
        w.WriteBytes(markCoverageBytes);

        w.Align2();
        w.DefineLabelHere(baseCoverageLabel);
        w.WriteBytes(baseCoverageBytes);

        // Write MarkArray.
        w.Align2();
        w.DefineLabelHere(markArrayLabel);
        int markArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)marks.Length));
        for (int i = 0; i < marks.Length; i++)
        {
            w.WriteUInt16(marks[i].Class);
            anchors.WriteOffset16(w, marks[i].Anchor, baseOffset: markArrayStart);
        }

        // Write BaseArray (AnchorMatrix).
        w.Align2();
        w.DefineLabelHere(baseArrayLabel);
        int baseArrayStart = w.Position;
        w.WriteUInt16(checked((ushort)baseGlyphCount));

        for (int r = 0; r < baseGlyphCount; r++)
        {
            ushort baseGlyphId = baseGlyphs[r];
            for (ushort c = 0; c < classCount; c++)
            {
                if (baseAnchorByKey.TryGetValue(Key(baseGlyphId, c), out var anchor))
                    anchors.WriteOffset16(w, anchor, baseOffset: baseArrayStart);
                else
                    w.WriteUInt16(0);
            }
        }

        // Emit anchors and device tables after arrays.
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

        for (int i = 0; i < _baseAnchors.Count; i++)
        {
            ushort c = _baseAnchors[i].Class;
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

    private readonly struct BaseAnchorEntry
    {
        public ushort GlyphId { get; }
        public ushort Class { get; }
        public AnchorTableBuilder Anchor { get; }

        public BaseAnchorEntry(ushort glyphId, ushort @class, AnchorTableBuilder anchor)
        {
            GlyphId = glyphId;
            Class = @class;
            Anchor = anchor;
        }
    }
}

