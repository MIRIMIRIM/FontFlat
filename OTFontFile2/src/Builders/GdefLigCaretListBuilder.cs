namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GDEF <c>LigCaretList</c> table.
/// </summary>
/// <remarks>
/// Supports CaretValue formats 1 (coordinate), 2 (point index), and 3 (coordinate + device/variation index).
/// </remarks>
public sealed class GdefLigCaretListBuilder
{
    private readonly List<LigGlyphEntry> _entries = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int LigGlyphCount => _entries.Count;

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        MarkDirty();
    }

    public void AddOrReplace(ushort ligGlyphId, ReadOnlySpan<CaretValue> carets)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].LigGlyphId == ligGlyphId)
                _entries.RemoveAt(i);
        }

        var copied = carets.ToArray();
        _entries.Add(new LigGlyphEntry(ligGlyphId, copied));
        MarkDirty();
    }

    public bool Remove(ushort ligGlyphId)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].LigGlyphId == ligGlyphId)
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
            throw new InvalidOperationException("LigCaretList ligGlyphCount must fit in uint16.");

        var entries = _entries.ToArray();
        Array.Sort(entries, static (a, b) => a.LigGlyphId.CompareTo(b.LigGlyphId));

        // Deduplicate (keep last).
        int uniqueCount = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (uniqueCount != 0 && entries[i].LigGlyphId == entries[uniqueCount - 1].LigGlyphId)
            {
                entries[uniqueCount - 1] = entries[i];
                continue;
            }

            entries[uniqueCount++] = entries[i];
        }

        int ligGlyphCount = uniqueCount;

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < ligGlyphCount; i++)
            coverage.AddGlyph(entries[i].LigGlyphId);
        byte[] coverageBytes = coverage.ToArray();

        int headerLen = checked(4 + (ligGlyphCount * 2));
        int coverageOffset = Align2(headerLen);
        int pos = checked(coverageOffset + coverageBytes.Length);

        Span<int> ligGlyphOffsets = ligGlyphCount <= 64 ? stackalloc int[ligGlyphCount] : new int[ligGlyphCount];
        var ligGlyphTables = new byte[ligGlyphCount][];

        for (int i = 0; i < ligGlyphCount; i++)
        {
            var carets = entries[i].Carets;
            if (carets.Length > ushort.MaxValue)
                throw new InvalidOperationException("LigGlyph caretCount must fit in uint16.");

            byte[] ligGlyphTable = BuildLigGlyphTable(carets);
            pos = Align2(pos);
            ligGlyphOffsets[i] = pos;
            ligGlyphTables[i] = ligGlyphTable;
            pos = checked(pos + ligGlyphTable.Length);
        }

        if (coverageOffset > ushort.MaxValue)
            throw new InvalidOperationException("LigCaretList coverageOffset must fit in uint16.");

        for (int i = 0; i < ligGlyphCount; i++)
        {
            if (ligGlyphOffsets[i] > ushort.MaxValue)
                throw new InvalidOperationException("LigCaretList ligGlyphOffset must fit in uint16.");
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, (ushort)coverageOffset);
        BigEndian.WriteUInt16(span, 2, (ushort)ligGlyphCount);
        for (int i = 0; i < ligGlyphCount; i++)
            BigEndian.WriteUInt16(span, 4 + (i * 2), (ushort)ligGlyphOffsets[i]);

        coverageBytes.CopyTo(span.Slice(coverageOffset));
        for (int i = 0; i < ligGlyphCount; i++)
            ligGlyphTables[i].CopyTo(span.Slice(ligGlyphOffsets[i]));

        return table;
    }

    private static byte[] BuildLigGlyphTable(ReadOnlySpan<CaretValue> carets)
    {
        int count = carets.Length;

        int headerLen = checked(2 + (count * 2));
        int pos = headerLen;

        Span<int> caretOffsets = count <= 32 ? stackalloc int[count] : new int[count];
        var caretTables = new byte[count][];

        for (int i = 0; i < count; i++)
        {
            byte[] caret = carets[i].ToTableBytes();
            pos = Align2(pos);
            caretOffsets[i] = pos;
            caretTables[i] = caret;
            pos = checked(pos + caret.Length);
        }

        for (int i = 0; i < count; i++)
        {
            if (caretOffsets[i] > ushort.MaxValue)
                throw new InvalidOperationException("CaretValue offset must fit in uint16.");
        }

        byte[] bytes = new byte[pos];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt16(span, 0, checked((ushort)count));
        for (int i = 0; i < count; i++)
            BigEndian.WriteUInt16(span, 2 + (i * 2), checked((ushort)caretOffsets[i]));

        for (int i = 0; i < count; i++)
            caretTables[i].CopyTo(span.Slice(caretOffsets[i]));

        return bytes;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    private readonly struct LigGlyphEntry
    {
        public ushort LigGlyphId { get; }
        public CaretValue[] Carets { get; }

        public LigGlyphEntry(ushort ligGlyphId, CaretValue[] carets)
        {
            LigGlyphId = ligGlyphId;
            Carets = carets;
        }
    }

    public readonly struct CaretValue
    {
        public ushort Format { get; }
        public short Coordinate { get; }
        public ushort PointIndex { get; }
        public DeviceTableBuilder? Device { get; }

        private CaretValue(ushort format, short coordinate, ushort pointIndex, DeviceTableBuilder? device)
        {
            Format = format;
            Coordinate = coordinate;
            PointIndex = pointIndex;
            Device = device;
        }

        public static CaretValue CoordinateValue(short coordinate) => new(format: 1, coordinate: coordinate, pointIndex: 0, device: null);

        public static CaretValue PointIndexValue(ushort pointIndex) => new(format: 2, coordinate: 0, pointIndex: pointIndex, device: null);

        public static CaretValue DeviceValue(short coordinate, DeviceTableBuilder device)
        {
            if (device is null) throw new ArgumentNullException(nameof(device));
            return new CaretValue(format: 3, coordinate: coordinate, pointIndex: 0, device: device);
        }

        internal byte[] ToTableBytes()
        {
            ushort format = Format;
            if (format == 1)
            {
                byte[] bytes = new byte[4];
                var span = bytes.AsSpan();
                BigEndian.WriteUInt16(span, 0, 1);
                BigEndian.WriteInt16(span, 2, Coordinate);
                return bytes;
            }

            if (format == 2)
            {
                byte[] bytes = new byte[4];
                var span = bytes.AsSpan();
                BigEndian.WriteUInt16(span, 0, 2);
                BigEndian.WriteUInt16(span, 2, PointIndex);
                return bytes;
            }

            if (format == 3)
            {
                if (Device is null)
                    throw new InvalidOperationException("CaretValue format 3 requires a DeviceTableBuilder.");

                var deviceBytes = Device.ToMemory().Span;

                const int headerLen = 6; // format(2) + coordinate(2) + deviceOffset(2)
                int deviceOffset = Align2(headerLen);

                byte[] bytes = new byte[checked(deviceOffset + deviceBytes.Length)];
                var span = bytes.AsSpan();

                BigEndian.WriteUInt16(span, 0, 3);
                BigEndian.WriteInt16(span, 2, Coordinate);
                BigEndian.WriteUInt16(span, 4, checked((ushort)deviceOffset));
                deviceBytes.CopyTo(span.Slice(deviceOffset));
                return bytes;
            }

            throw new InvalidOperationException("Unsupported CaretValue format. Only formats 1, 2, and 3 are supported.");
        }
    }
}
