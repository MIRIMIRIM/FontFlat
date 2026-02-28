namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS SinglePos subtables (lookup type 1).
/// </summary>
public sealed class GposSinglePosSubtableBuilder
{
    private bool _useFormat1;
    private readonly CoverageTableBuilder _coverage = new();
    private readonly GposValueRecordBuilder _format1Value = new();

    private readonly List<Entry> _format2Entries = new();

    private bool _dirty = true;
    private byte[]? _built;

    public ushort PosFormat => _useFormat1 ? (ushort)1 : (ushort)2;

    public void Clear()
    {
        _useFormat1 = false;
        _coverage.Clear();
        _format1Value.Clear();
        _format2Entries.Clear();
        MarkDirty();
    }

    public void SetFormat1(CoverageTableBuilder coverage, GposValueRecordBuilder value)
    {
        if (coverage is null) throw new ArgumentNullException(nameof(coverage));
        if (value is null) throw new ArgumentNullException(nameof(value));

        _useFormat1 = true;

        _coverage.Clear();
        byte[] covBytes = coverage.ToArray();
        _coverage.AddGlyphs(ExtractCoverageGlyphs(covBytes));

        _format1Value.Clear();
        CopyValue(value, _format1Value);

        _format2Entries.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort glyphId, GposValueRecordBuilder value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        _useFormat1 = false;

        for (int i = _format2Entries.Count - 1; i >= 0; i--)
        {
            if (_format2Entries[i].GlyphId == glyphId)
                _format2Entries.RemoveAt(i);
        }

        var copy = new GposValueRecordBuilder();
        CopyValue(value, copy);
        _format2Entries.Add(new Entry(glyphId, copy));
        MarkDirty();
    }

    public bool Remove(ushort glyphId)
    {
        if (_useFormat1)
            return false;

        bool removed = false;
        for (int i = _format2Entries.Count - 1; i >= 0; i--)
        {
            if (_format2Entries[i].GlyphId == glyphId)
            {
                _format2Entries.RemoveAt(i);
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
        if (_useFormat1)
            return BuildFormat1();

        return BuildFormat2();
    }

    private byte[] BuildFormat1()
    {
        ushort valueFormat = _format1Value.GetValueFormat();
        byte[] coverageBytes = _coverage.ToArray();

        var w = new OTFontFile2.OffsetWriter();
        var devices = new DeviceTablePool();
        var coverageLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteUInt16(valueFormat);

        _format1Value.WriteTo(w, valueFormat, posTableBaseOffset: 0, devices);

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        devices.EmitAllAligned2(w);
        return w.ToArray();
    }

    private byte[] BuildFormat2()
    {
        if (_format2Entries.Count == 0)
        {
            var emptyCoverage = new CoverageTableBuilder();
            byte[] emptyCoverageBytes = emptyCoverage.ToArray();

            var w0 = new OTFontFile2.OffsetWriter();
            var coverageLabel0 = w0.CreateLabel();
            w0.WriteUInt16(2);
            w0.WriteOffset16(coverageLabel0, baseOffset: 0);
            w0.WriteUInt16(0);
            w0.WriteUInt16(0);
            w0.Align2();
            w0.DefineLabelHere(coverageLabel0);
            w0.WriteBytes(emptyCoverageBytes);
            return w0.ToArray();
        }

        var entries = _format2Entries.ToArray();
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
            throw new InvalidOperationException("valueCount must fit in uint16.");

        ushort valueFormat = 0;
        for (int i = 0; i < uniqueCount; i++)
            valueFormat |= entries[i].Value.GetValueFormat();

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < uniqueCount; i++)
            coverage.AddGlyph(entries[i].GlyphId);
        byte[] coverageBytes = coverage.ToArray();

        var w = new OTFontFile2.OffsetWriter();
        var devices = new DeviceTablePool();
        var coverageLabel = w.CreateLabel();

        w.WriteUInt16(2);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteUInt16(valueFormat);
        w.WriteUInt16(checked((ushort)uniqueCount));

        for (int i = 0; i < uniqueCount; i++)
            entries[i].Value.WriteTo(w, valueFormat, posTableBaseOffset: 0, devices);

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        devices.EmitAllAligned2(w);
        return w.ToArray();
    }

    private static ushort[] ExtractCoverageGlyphs(byte[] coverageBytes)
    {
        if (coverageBytes.Length < 4)
            return Array.Empty<ushort>();

        ushort format = BigEndian.ReadUInt16(coverageBytes, 0);
        ushort count = BigEndian.ReadUInt16(coverageBytes, 2);

        if (format == 1)
        {
            int needed = 4 + (count * 2);
            if (coverageBytes.Length < needed)
                return Array.Empty<ushort>();

            var glyphs = new ushort[count];
            int o = 4;
            for (int i = 0; i < count; i++)
            {
                glyphs[i] = BigEndian.ReadUInt16(coverageBytes, o);
                o += 2;
            }
            return glyphs;
        }

        if (format == 2)
        {
            int needed = 4 + (count * 6);
            if (coverageBytes.Length < needed)
                return Array.Empty<ushort>();

            var list = new List<ushort>();
            int o = 4;
            for (int i = 0; i < count; i++)
            {
                ushort start = BigEndian.ReadUInt16(coverageBytes, o + 0);
                ushort end = BigEndian.ReadUInt16(coverageBytes, o + 2);
                o += 6;

                if (end < start)
                    continue;

                for (ushort gid = start; gid <= end; gid++)
                {
                    list.Add(gid);
                    if (gid == ushort.MaxValue)
                        break;
                }
            }

            return list.ToArray();
        }

        return Array.Empty<ushort>();
    }

    private static void CopyValue(GposValueRecordBuilder source, GposValueRecordBuilder dest)
    {
        dest.Clear();

        if (source.HasXPlacement) dest.XPlacement = source.XPlacement;
        if (source.HasYPlacement) dest.YPlacement = source.YPlacement;
        if (source.HasXAdvance) dest.XAdvance = source.XAdvance;
        if (source.HasYAdvance) dest.YAdvance = source.YAdvance;

        dest.XPlacementDevice = source.XPlacementDevice;
        dest.YPlacementDevice = source.YPlacementDevice;
        dest.XAdvanceDevice = source.XAdvanceDevice;
        dest.YAdvanceDevice = source.YAdvanceDevice;
    }

    private readonly struct Entry
    {
        public ushort GlyphId { get; }
        public GposValueRecordBuilder Value { get; }

        public Entry(ushort glyphId, GposValueRecordBuilder value)
        {
            GlyphId = glyphId;
            Value = value;
        }
    }

}
