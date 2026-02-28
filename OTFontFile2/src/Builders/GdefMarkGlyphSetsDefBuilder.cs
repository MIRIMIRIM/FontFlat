namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GDEF <c>MarkGlyphSetsDef</c> table (GDEF v1.2+).
/// </summary>
public sealed class GdefMarkGlyphSetsDefBuilder
{
    private const ushort SupportedFormat = 1;

    private readonly List<ReadOnlyMemory<byte>> _coverageTables = new();

    private bool _dirty = true;
    private byte[]? _built;

    public ushort Format => SupportedFormat;

    public int MarkGlyphSetCount => _coverageTables.Count;

    public IReadOnlyList<ReadOnlyMemory<byte>> CoverageTables => _coverageTables;

    public void Clear()
    {
        if (_coverageTables.Count == 0)
            return;

        _coverageTables.Clear();
        MarkDirty();
    }

    public void AddGlyphSet(CoverageTableBuilder coverage)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));

        _coverageTables.Add(coverage.ToMemory());
        MarkDirty();
    }

    public void AddGlyphSetData(ReadOnlyMemory<byte> coverageTableBytes)
    {
        if (coverageTableBytes.Length < 4)
            throw new ArgumentException("Coverage table must be at least 4 bytes.", nameof(coverageTableBytes));

        _coverageTables.Add(coverageTableBytes);
        MarkDirty();
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    public static bool TryFrom(GdefMarkGlyphSetsDefTable markGlyphSetsDef, out GdefMarkGlyphSetsDefBuilder builder)
    {
        builder = null!;

        if (markGlyphSetsDef.MarkGlyphSetsDefFormat != SupportedFormat)
            return false;

        ushort count = markGlyphSetsDef.MarkGlyphSetCount;
        var b = new GdefMarkGlyphSetsDefBuilder();

        for (int i = 0; i < count; i++)
        {
            if (!markGlyphSetsDef.TryGetCoverageTable(i, out var coverage))
                return false;

            var span = coverage.Table.Span;
            int start = coverage.Offset;

            // Determine a safe upper bound by walking offsets, similar to other "sectioned" builders.
            // We only need to preserve bytes for roundtrip; validation occurs on read.
            if ((uint)start >= (uint)span.Length)
                return false;

            // Best-effort: read format and compute minimal length.
            ushort fmt = coverage.CoverageFormat;
            int length;
            if (fmt == 1)
            {
                if ((uint)start > (uint)span.Length - 4)
                    return false;
                ushort glyphCount = BigEndian.ReadUInt16(span, start + 2);
                length = 4 + (glyphCount * 2);
            }
            else if (fmt == 2)
            {
                if ((uint)start > (uint)span.Length - 4)
                    return false;
                ushort rangeCount = BigEndian.ReadUInt16(span, start + 2);
                length = 4 + (rangeCount * 6);
            }
            else
            {
                return false;
            }

            if ((uint)start > (uint)span.Length - (uint)length)
                return false;

            b._coverageTables.Add(span.Slice(start, length).ToArray());
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

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        int count = _coverageTables.Count;
        if (count > ushort.MaxValue)
            throw new InvalidOperationException("MarkGlyphSetCount must fit in uint16.");

        int headerLen = checked(4 + (count * 4));

        int pos = headerLen;
        Span<int> offsets = count <= 64 ? stackalloc int[count] : new int[count];

        for (int i = 0; i < count; i++)
        {
            var cov = _coverageTables[i];
            if (cov.Length < 4)
                throw new InvalidOperationException("Coverage table must be at least 4 bytes.");

            pos = Align2(pos);
            offsets[i] = pos;
            pos = checked(pos + cov.Length);
        }

        byte[] bytes = new byte[pos];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt16(span, 0, SupportedFormat);
        BigEndian.WriteUInt16(span, 2, (ushort)count);

        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt32(span, 4 + (i * 4), (uint)offsets[i]);
            _coverageTables[i].Span.CopyTo(span.Slice(offsets[i]));
        }

        return bytes;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;
}

