namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GSUB SingleSubst subtables (lookup type 1).
/// Builds format 1 when all substitutions share a constant delta, otherwise format 2.
/// </summary>
public sealed class GsubSingleSubstSubtableBuilder
{
    private readonly List<SubstPair> _pairs = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int PairCount => _pairs.Count;

    public void Clear()
    {
        if (_pairs.Count == 0)
            return;

        _pairs.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort fromGlyphId, ushort toGlyphId)
    {
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].FromGlyphId == fromGlyphId)
                _pairs.RemoveAt(i);
        }

        _pairs.Add(new SubstPair(fromGlyphId, toGlyphId));
        MarkDirty();
    }

    public bool Remove(ushort fromGlyphId)
    {
        bool removed = false;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].FromGlyphId == fromGlyphId)
            {
                _pairs.RemoveAt(i);
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

        if (_pairs.Count == 0)
        {
            w.WriteUInt16(2);
            w.WriteOffset16(coverageLabel, baseOffset: 0);
            w.WriteUInt16(0);
            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(new CoverageTableBuilder().ToArray());
            return w.ToArray();
        }

        var pairs = _pairs.ToArray();
        Array.Sort(pairs, static (a, b) => a.FromGlyphId.CompareTo(b.FromGlyphId));

        int uniqueCount = 1;
        for (int i = 1; i < pairs.Length; i++)
        {
            if (pairs[i].FromGlyphId == pairs[uniqueCount - 1].FromGlyphId)
            {
                pairs[uniqueCount - 1] = pairs[i];
                continue;
            }

            pairs[uniqueCount++] = pairs[i];
        }

        if (uniqueCount > ushort.MaxValue)
            throw new InvalidOperationException("SingleSubst glyphCount must fit in uint16.");

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < uniqueCount; i++)
            coverage.AddGlyph(pairs[i].FromGlyphId);

        byte[] coverageBytes = coverage.ToArray();

        bool canUseFormat1 = true;
        int delta = (int)pairs[0].ToGlyphId - pairs[0].FromGlyphId;
        if ((short)delta != delta)
            canUseFormat1 = false;
        else
        {
            short deltaShort = (short)delta;
            for (int i = 1; i < uniqueCount; i++)
            {
                ushort from = pairs[i].FromGlyphId;
                ushort expected = unchecked((ushort)(from + deltaShort));
                if (expected != pairs[i].ToGlyphId)
                {
                    canUseFormat1 = false;
                    break;
                }
            }
        }

        if (canUseFormat1)
        {
            w.WriteUInt16(1);
            w.WriteOffset16(coverageLabel, baseOffset: 0);
            w.WriteInt16(checked((short)delta));
            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(coverageBytes);
            return w.ToArray();
        }

        w.WriteUInt16(2);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteUInt16(checked((ushort)uniqueCount));
        for (int i = 0; i < uniqueCount; i++)
            w.WriteUInt16(pairs[i].ToGlyphId);
        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);
        return w.ToArray();
    }

    private readonly struct SubstPair
    {
        public ushort FromGlyphId { get; }
        public ushort ToGlyphId { get; }

        public SubstPair(ushort fromGlyphId, ushort toGlyphId)
        {
            FromGlyphId = fromGlyphId;
            ToGlyphId = toGlyphId;
        }
    }
}
