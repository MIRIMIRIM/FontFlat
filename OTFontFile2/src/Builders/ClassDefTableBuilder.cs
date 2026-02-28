namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for OpenType <c>ClassDef</c> tables (used by GDEF/GSUB/GPOS).
/// </summary>
/// <remarks>
/// This builder emits ClassDef format 2 (range records). Unspecified glyphs are implicitly class 0.
/// </remarks>
public sealed class ClassDefTableBuilder
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

    public bool TryGetClass(ushort glyphId, out ushort classValue)
    {
        classValue = 0;

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.GlyphId == glyphId)
            {
                classValue = e.ClassValue;
                return true;
            }
        }

        return true;
    }

    public void SetClass(ushort glyphId, ushort classValue)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var existing = _entries[i];
            if (existing.GlyphId != glyphId)
                continue;

            if (classValue == 0)
            {
                _entries.RemoveAt(i);
                MarkDirty();
                return;
            }

            if (existing.ClassValue == classValue)
                return;

            _entries[i] = new Entry(glyphId, classValue);
            MarkDirty();
            return;
        }

        if (classValue == 0)
            return;

        _entries.Add(new Entry(glyphId, classValue));
        MarkDirty();
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    public static bool TryFrom(ClassDefTable classDef, out ClassDefTableBuilder builder)
    {
        builder = null!;

        ushort format = classDef.ClassFormat;
        if (format is not (1 or 2))
            return false;

        var b = new ClassDefTableBuilder();

        if (format == 1)
        {
            var data = classDef.Table.Span;
            int offset = classDef.Offset;

            if ((uint)offset > (uint)data.Length - 6)
                return false;

            ushort startGlyphId = BigEndian.ReadUInt16(data, offset + 2);
            ushort glyphCount = BigEndian.ReadUInt16(data, offset + 4);

            int required = 6 + (glyphCount * 2);
            if ((uint)offset > (uint)data.Length - (uint)required)
                return false;

            int arrayOffset = offset + 6;
            for (int i = 0; i < glyphCount; i++)
            {
                ushort classValue = BigEndian.ReadUInt16(data, arrayOffset + (i * 2));
                if (classValue == 0)
                    continue;

                ushort glyphId = (ushort)(startGlyphId + i);
                b._entries.Add(new Entry(glyphId, classValue));
            }
        }
        else
        {
            if (!classDef.TryGetRangeCount(out ushort rangeCount))
                return false;

            for (int i = 0; i < rangeCount; i++)
            {
                if (!classDef.TryGetRangeRecord(i, out var r))
                    return false;

                if (r.Class == 0)
                    continue;

                if (r.EndGlyphId < r.StartGlyphId)
                    return false;

                for (ushort gid = r.StartGlyphId; gid <= r.EndGlyphId; gid++)
                {
                    b._entries.Add(new Entry(gid, r.Class));
                    if (gid == ushort.MaxValue)
                        break;
                }
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildClassDefFormat2Bytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildClassDefFormat2Bytes()
    {
        if (_entries.Count == 0)
        {
            byte[] empty = new byte[4];
            var span = empty.AsSpan();
            BigEndian.WriteUInt16(span, 0, 2); // classFormat
            BigEndian.WriteUInt16(span, 2, 0); // rangeCount
            return empty;
        }

        var entries = _entries.ToArray();
        Array.Sort(entries, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));

        // Deduplicate and validate.
        int uniqueCount = 1;
        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].GlyphId == entries[uniqueCount - 1].GlyphId)
            {
                // Keep the last one.
                entries[uniqueCount - 1] = entries[i];
                continue;
            }

            entries[uniqueCount++] = entries[i];
        }

        // Build ranges of consecutive glyphs with same class.
        Span<(ushort start, ushort end, ushort cls)> ranges = uniqueCount <= 128
            ? stackalloc (ushort start, ushort end, ushort cls)[uniqueCount]
            : new (ushort start, ushort end, ushort cls)[uniqueCount];

        int rangeCount = 0;

        ushort currentStart = entries[0].GlyphId;
        ushort currentEnd = entries[0].GlyphId;
        ushort currentClass = entries[0].ClassValue;

        for (int i = 1; i < uniqueCount; i++)
        {
            var e = entries[i];

            bool isConsecutive = e.GlyphId == (ushort)(currentEnd + 1);
            bool sameClass = e.ClassValue == currentClass;

            if (isConsecutive && sameClass)
            {
                currentEnd = e.GlyphId;
                continue;
            }

            ranges[rangeCount++] = (currentStart, currentEnd, currentClass);
            currentStart = e.GlyphId;
            currentEnd = e.GlyphId;
            currentClass = e.ClassValue;
        }

        ranges[rangeCount++] = (currentStart, currentEnd, currentClass);

        if (rangeCount > ushort.MaxValue)
            throw new InvalidOperationException("ClassDef rangeCount must fit in uint16.");

        int length = checked(4 + (rangeCount * 6));
        byte[] table = new byte[length];
        var spanOut = table.AsSpan();

        BigEndian.WriteUInt16(spanOut, 0, 2); // classFormat
        BigEndian.WriteUInt16(spanOut, 2, (ushort)rangeCount);

        int o = 4;
        for (int i = 0; i < rangeCount; i++)
        {
            var r = ranges[i];
            BigEndian.WriteUInt16(spanOut, o + 0, r.start);
            BigEndian.WriteUInt16(spanOut, o + 2, r.end);
            BigEndian.WriteUInt16(spanOut, o + 4, r.cls);
            o += 6;
        }

        return table;
    }

    private readonly struct Entry
    {
        public ushort GlyphId { get; }
        public ushort ClassValue { get; }

        public Entry(ushort glyphId, ushort classValue)
        {
            GlyphId = glyphId;
            ClassValue = classValue;
        }
    }
}
