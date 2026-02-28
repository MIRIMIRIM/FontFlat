namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for OpenType <c>Coverage</c> tables (used by GSUB/GPOS).
/// </summary>
public sealed class CoverageTableBuilder
{
    private readonly List<ushort> _glyphIds = new();
    private bool _dirty = true;
    private byte[]? _built;

    public int GlyphCount => _glyphIds.Count;

    public void Clear()
    {
        if (_glyphIds.Count == 0)
            return;

        _glyphIds.Clear();
        MarkDirty();
    }

    public void AddGlyph(ushort glyphId)
    {
        _glyphIds.Add(glyphId);
        MarkDirty();
    }

    public void AddGlyphs(ReadOnlySpan<ushort> glyphIds)
    {
        if (glyphIds.Length == 0)
            return;

        for (int i = 0; i < glyphIds.Length; i++)
            _glyphIds.Add(glyphIds[i]);

        MarkDirty();
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    public static bool TryFrom(CoverageTable coverage, out CoverageTableBuilder builder)
    {
        builder = null!;

        ushort format = coverage.CoverageFormat;
        if (format is not (1 or 2))
            return false;

        var b = new CoverageTableBuilder();

        if (format == 1)
        {
            if (!coverage.TryGetFormat1GlyphCount(out ushort glyphCount))
                return false;

            for (int i = 0; i < glyphCount; i++)
            {
                if (!coverage.TryGetFormat1GlyphId(i, out ushort gid))
                    return false;

                b._glyphIds.Add(gid);
            }
        }
        else
        {
            if (!coverage.TryGetFormat2RangeCount(out ushort rangeCount))
                return false;

            for (int i = 0; i < rangeCount; i++)
            {
                if (!coverage.TryGetFormat2RangeRecord(i, out var r))
                    return false;

                if (r.EndGlyphId < r.StartGlyphId)
                    return false;

                for (ushort gid = r.StartGlyphId; gid <= r.EndGlyphId; gid++)
                {
                    b._glyphIds.Add(gid);
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

        _built = BuildCoverageBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildCoverageBytes()
    {
        if (_glyphIds.Count == 0)
        {
            byte[] empty = new byte[4];
            var span = empty.AsSpan();
            BigEndian.WriteUInt16(span, 0, 1); // CoverageFormat
            BigEndian.WriteUInt16(span, 2, 0); // glyphCount
            return empty;
        }

        var glyphs = _glyphIds.ToArray();
        Array.Sort(glyphs);

        // Deduplicate.
        int uniqueCount = 1;
        for (int i = 1; i < glyphs.Length; i++)
        {
            if (glyphs[i] == glyphs[uniqueCount - 1])
                continue;

            glyphs[uniqueCount++] = glyphs[i];
        }

        if (uniqueCount > ushort.MaxValue)
            throw new InvalidOperationException("Coverage glyphCount must fit in uint16.");

        // Build ranges for format 2 and choose the smaller encoding.
        Span<(ushort start, ushort end)> ranges = uniqueCount <= 256
            ? stackalloc (ushort start, ushort end)[uniqueCount]
            : new (ushort start, ushort end)[uniqueCount];

        int rangeCount = 0;
        ushort start = glyphs[0];
        ushort end = glyphs[0];
        for (int i = 1; i < uniqueCount; i++)
        {
            ushort gid = glyphs[i];
            if (gid == (ushort)(end + 1))
            {
                end = gid;
                continue;
            }

            ranges[rangeCount++] = (start, end);
            start = gid;
            end = gid;
        }

        ranges[rangeCount++] = (start, end);

        int sizeFormat1 = checked(4 + (uniqueCount * 2));
        int sizeFormat2 = checked(4 + (rangeCount * 6));

        if (sizeFormat1 <= sizeFormat2)
            return BuildFormat1(glyphs.AsSpan(0, uniqueCount));

        return BuildFormat2(ranges.Slice(0, rangeCount));
    }

    private static byte[] BuildFormat1(ReadOnlySpan<ushort> glyphs)
    {
        int count = glyphs.Length;
        byte[] table = new byte[checked(4 + (count * 2))];
        var span = table.AsSpan();
        BigEndian.WriteUInt16(span, 0, 1);
        BigEndian.WriteUInt16(span, 2, checked((ushort)count));
        int o = 4;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt16(span, o, glyphs[i]);
            o += 2;
        }

        return table;
    }

    private static byte[] BuildFormat2(ReadOnlySpan<(ushort start, ushort end)> ranges)
    {
        int rangeCount = ranges.Length;
        if (rangeCount > ushort.MaxValue)
            throw new InvalidOperationException("Coverage rangeCount must fit in uint16.");

        byte[] table = new byte[checked(4 + (rangeCount * 6))];
        var span = table.AsSpan();
        BigEndian.WriteUInt16(span, 0, 2);
        BigEndian.WriteUInt16(span, 2, checked((ushort)rangeCount));

        ushort coverageIndex = 0;
        int o = 4;
        for (int i = 0; i < rangeCount; i++)
        {
            var r = ranges[i];
            if (r.end < r.start)
                throw new InvalidOperationException("Coverage range end must be >= start.");

            BigEndian.WriteUInt16(span, o + 0, r.start);
            BigEndian.WriteUInt16(span, o + 2, r.end);
            BigEndian.WriteUInt16(span, o + 4, coverageIndex);

            uint rangeLen = (uint)(r.end - r.start) + 1u;
            if (rangeLen > ushort.MaxValue)
                throw new InvalidOperationException("Coverage range length must fit in uint16.");

            coverageIndex = checked((ushort)(coverageIndex + (ushort)rangeLen));
            o += 6;
        }

        return table;
    }
}

