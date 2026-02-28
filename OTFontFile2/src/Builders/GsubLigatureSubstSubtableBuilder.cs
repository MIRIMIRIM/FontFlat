namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GSUB LigatureSubst subtables (lookup type 4), format 1.
/// </summary>
public sealed class GsubLigatureSubstSubtableBuilder
{
    private readonly List<LigatureEntry> _ligatures = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int LigatureCount => _ligatures.Count;

    public void Clear()
    {
        if (_ligatures.Count == 0)
            return;

        _ligatures.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort firstGlyphId, ReadOnlySpan<ushort> remainingComponents, ushort ligatureGlyphId)
    {
        if (remainingComponents.Length == 0)
            throw new ArgumentException("LigatureSubst requires at least 2 components.", nameof(remainingComponents));

        for (int i = _ligatures.Count - 1; i >= 0; i--)
        {
            var e = _ligatures[i];
            if (e.FirstGlyphId == firstGlyphId && ComponentsEqual(e.Components, remainingComponents))
                _ligatures.RemoveAt(i);
        }

        _ligatures.Add(new LigatureEntry(firstGlyphId, remainingComponents.ToArray(), ligatureGlyphId));
        MarkDirty();
    }

    public void AddOrReplace(ReadOnlySpan<ushort> glyphSequence, ushort ligatureGlyphId)
    {
        if (glyphSequence.Length < 2)
            throw new ArgumentException("Ligature glyph sequence must have at least 2 components.", nameof(glyphSequence));

        ushort first = glyphSequence[0];
        AddOrReplace(first, glyphSequence.Slice(1), ligatureGlyphId);
    }

    public bool Remove(ushort firstGlyphId, ReadOnlySpan<ushort> remainingComponents)
    {
        bool removed = false;
        for (int i = _ligatures.Count - 1; i >= 0; i--)
        {
            var e = _ligatures[i];
            if (e.FirstGlyphId == firstGlyphId && ComponentsEqual(e.Components, remainingComponents))
            {
                _ligatures.RemoveAt(i);
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

        if (_ligatures.Count == 0)
        {
            w.WriteUInt16(0);
            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(new CoverageTableBuilder().ToArray());
            return w.ToArray();
        }

        var ligs = _ligatures.ToArray();
        Array.Sort(ligs, static (a, b) =>
        {
            int c = a.FirstGlyphId.CompareTo(b.FirstGlyphId);
            if (c != 0) return c;
            return CompareComponents(a.Components, b.Components);
        });

        // Deduplicate (keep last).
        int uniqueCount = 1;
        for (int i = 1; i < ligs.Length; i++)
        {
            if (ligs[i].FirstGlyphId == ligs[uniqueCount - 1].FirstGlyphId && CompareComponents(ligs[i].Components, ligs[uniqueCount - 1].Components) == 0)
            {
                ligs[uniqueCount - 1] = ligs[i];
                continue;
            }

            ligs[uniqueCount++] = ligs[i];
        }

        if (uniqueCount != ligs.Length)
            Array.Resize(ref ligs, uniqueCount);

        // Unique first glyphs and group ranges.
        Span<ushort> firstGlyphs = uniqueCount <= 512 ? stackalloc ushort[uniqueCount] : new ushort[uniqueCount];
        Span<int> groupStarts = uniqueCount <= 512 ? stackalloc int[uniqueCount] : new int[uniqueCount];
        Span<int> groupCounts = uniqueCount <= 512 ? stackalloc int[uniqueCount] : new int[uniqueCount];

        int groupCount = 0;
        int idx = 0;
        while (idx < uniqueCount)
        {
            ushort first = ligs[idx].FirstGlyphId;
            firstGlyphs[groupCount] = first;
            groupStarts[groupCount] = idx;

            int j = idx;
            while (j < uniqueCount && ligs[j].FirstGlyphId == first)
                j++;

            groupCounts[groupCount] = j - idx;
            groupCount++;
            idx = j;
        }

        if (groupCount > ushort.MaxValue)
            throw new InvalidOperationException("LigatureSetCount must fit in uint16.");

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < groupCount; i++)
            coverage.AddGlyph(firstGlyphs[i]);
        byte[] coverageBytes = coverage.ToArray();

        w.WriteUInt16(checked((ushort)groupCount));

        Span<OTFontFile2.OffsetWriter.Label> setLabels = groupCount <= 128
            ? stackalloc OTFontFile2.OffsetWriter.Label[groupCount]
            : new OTFontFile2.OffsetWriter.Label[groupCount];

        for (int i = 0; i < groupCount; i++)
        {
            var label = w.CreateLabel();
            setLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        OTFontFile2.OffsetWriter.Label[]? ligLabelsScratch = null;

        for (int i = 0; i < groupCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(setLabels[i]);
            int setStart = w.Position;

            int start = groupStarts[i];
            int count = groupCounts[i];
            if (count > ushort.MaxValue)
                throw new InvalidOperationException("LigatureCount must fit in uint16.");

            w.WriteUInt16(checked((ushort)count));

            var ligLabelsArray = ligLabelsScratch;
            if (ligLabelsArray is null || ligLabelsArray.Length < count)
            {
                int newSize = ligLabelsArray is null ? 64 : ligLabelsArray.Length;
                if (newSize < 64) newSize = 64;
                while (newSize < count)
                    newSize = checked(newSize * 2);

                ligLabelsArray = new OTFontFile2.OffsetWriter.Label[newSize];
                ligLabelsScratch = ligLabelsArray;
            }

            var ligLabels = ligLabelsArray.AsSpan(0, count);

            for (int l = 0; l < count; l++)
            {
                var label = w.CreateLabel();
                ligLabels[l] = label;
                w.WriteOffset16(label, baseOffset: setStart);
            }

            for (int l = 0; l < count; l++)
            {
                w.Align2();
                w.DefineLabelHere(ligLabels[l]);

                var lig = ligs[start + l];
                int componentCount = lig.Components.Length + 1;
                if (componentCount < 2 || componentCount > ushort.MaxValue)
                    throw new InvalidOperationException("ComponentCount must be between 2 and 65535.");

                w.WriteUInt16(lig.LigatureGlyphId);
                w.WriteUInt16(checked((ushort)componentCount));

                for (int c = 0; c < lig.Components.Length; c++)
                    w.WriteUInt16(lig.Components[c]);
            }
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        return w.ToArray();
    }

    private static bool ComponentsEqual(ushort[] components, ReadOnlySpan<ushort> other)
    {
        if (components.Length != other.Length)
            return false;

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != other[i])
                return false;
        }

        return true;
    }

    private static int CompareComponents(ushort[] a, ushort[] b)
    {
        int min = a.Length < b.Length ? a.Length : b.Length;
        for (int i = 0; i < min; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0)
                return c;
        }

        return a.Length.CompareTo(b.Length);
    }

    private readonly struct LigatureEntry
    {
        public ushort FirstGlyphId { get; }
        public ushort[] Components { get; }
        public ushort LigatureGlyphId { get; }

        public LigatureEntry(ushort firstGlyphId, ushort[] components, ushort ligatureGlyphId)
        {
            FirstGlyphId = firstGlyphId;
            Components = components;
            LigatureGlyphId = ligatureGlyphId;
        }
    }
}
