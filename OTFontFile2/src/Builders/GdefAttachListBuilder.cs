namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GDEF <c>AttachList</c> table.
/// </summary>
public sealed class GdefAttachListBuilder
{
    private readonly List<Entry> _entries = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int GlyphCount => _entries.Count;

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort glyphId, ReadOnlySpan<ushort> pointIndices)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].GlyphId == glyphId)
                _entries.RemoveAt(i);
        }

        _entries.Add(new Entry(glyphId, pointIndices.ToArray()));
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
        if (_entries.Count > ushort.MaxValue)
            throw new InvalidOperationException("AttachList glyphCount must fit in uint16.");

        var entries = _entries.ToArray();
        Array.Sort(entries, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));

        // Deduplicate glyph IDs (keep last).
        int uniqueCount = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (uniqueCount != 0 && entries[i].GlyphId == entries[uniqueCount - 1].GlyphId)
            {
                entries[uniqueCount - 1] = entries[i];
                continue;
            }

            entries[uniqueCount++] = entries[i];
        }

        int glyphCount = uniqueCount;

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < glyphCount; i++)
            coverage.AddGlyph(entries[i].GlyphId);
        byte[] coverageBytes = coverage.ToArray();

        int headerLen = checked(4 + (glyphCount * 2));
        int coverageOffset = Align2(headerLen);
        int pos = checked(coverageOffset + coverageBytes.Length);

        Span<int> attachPointOffsets = glyphCount <= 64 ? stackalloc int[glyphCount] : new int[glyphCount];
        var attachPointTables = new byte[glyphCount][];

        for (int i = 0; i < glyphCount; i++)
        {
            ushort[] points = entries[i].PointIndices;
            if (points.Length > ushort.MaxValue)
                throw new InvalidOperationException("AttachPointTable pointCount must fit in uint16.");

            if (points.Length > 1)
            {
                Array.Sort(points);
                points = DeduplicateSorted(points);
            }

            byte[] ap = BuildAttachPointTable(points);
            pos = Align2(pos);
            attachPointOffsets[i] = pos;
            attachPointTables[i] = ap;
            pos = checked(pos + ap.Length);
        }

        if (coverageOffset > ushort.MaxValue)
            throw new InvalidOperationException("AttachList coverageOffset must fit in uint16.");

        for (int i = 0; i < glyphCount; i++)
        {
            if (attachPointOffsets[i] > ushort.MaxValue)
                throw new InvalidOperationException("AttachList attachPointOffset must fit in uint16.");
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, (ushort)coverageOffset);
        BigEndian.WriteUInt16(span, 2, (ushort)glyphCount);
        for (int i = 0; i < glyphCount; i++)
            BigEndian.WriteUInt16(span, 4 + (i * 2), (ushort)attachPointOffsets[i]);

        coverageBytes.CopyTo(span.Slice(coverageOffset));
        for (int i = 0; i < glyphCount; i++)
            attachPointTables[i].CopyTo(span.Slice(attachPointOffsets[i]));

        return table;
    }

    private static byte[] BuildAttachPointTable(ReadOnlySpan<ushort> pointIndices)
    {
        int count = pointIndices.Length;
        byte[] bytes = new byte[checked(2 + (count * 2))];
        var span = bytes.AsSpan();
        BigEndian.WriteUInt16(span, 0, checked((ushort)count));
        int o = 2;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt16(span, o, pointIndices[i]);
            o += 2;
        }

        return bytes;
    }

    private static ushort[] DeduplicateSorted(ushort[] sorted)
    {
        int count = sorted.Length;
        if (count <= 1)
            return sorted;

        int unique = 1;
        for (int i = 1; i < count; i++)
        {
            if (sorted[i] == sorted[unique - 1])
                continue;

            sorted[unique++] = sorted[i];
        }

        if (unique == count)
            return sorted;

        var trimmed = new ushort[unique];
        Array.Copy(sorted, 0, trimmed, 0, unique);
        return trimmed;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    private readonly struct Entry
    {
        public ushort GlyphId { get; }
        public ushort[] PointIndices { get; }

        public Entry(ushort glyphId, ushort[] pointIndices)
        {
            GlyphId = glyphId;
            PointIndices = pointIndices;
        }
    }
}
