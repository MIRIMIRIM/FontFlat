using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>hdmx</c> table.
/// </summary>
[OtTableBuilder("hdmx")]
public sealed partial class HdmxTableBuilder : ISfntTableSource
{
    private readonly List<DeviceRecordEntry> _records = new();
    private readonly ushort _numGlyphs;

    private ushort _version;

    public HdmxTableBuilder(ushort numGlyphs)
    {
        _numGlyphs = numGlyphs;
    }

    public ushort Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public ushort NumGlyphs => _numGlyphs;

    public int RecordCount => _records.Count;

    public IReadOnlyList<DeviceRecordEntry> Records => _records;

    public void Clear()
    {
        _records.Clear();
        MarkDirty();
    }

    public void AddOrReplaceRecord(byte pixelSize, ReadOnlySpan<byte> widths)
    {
        if (widths.Length != _numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(widths), $"Widths length must be exactly {NumGlyphs}.");

        for (int i = _records.Count - 1; i >= 0; i--)
        {
            if (_records[i].PixelSize == pixelSize)
                _records.RemoveAt(i);
        }

        byte maxWidth = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            byte w = widths[i];
            if (w > maxWidth)
                maxWidth = w;
        }

        _records.Add(new DeviceRecordEntry(pixelSize, maxWidth, widths.ToArray()));
        MarkDirty();
    }

    public bool RemoveRecord(byte pixelSize)
    {
        bool removed = false;
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            if (_records[i].PixelSize == pixelSize)
            {
                _records.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public static bool TryFrom(HdmxTable hdmx, ushort numGlyphs, out HdmxTableBuilder builder)
    {
        builder = null!;

        var b = new HdmxTableBuilder(numGlyphs)
        {
            Version = hdmx.Version
        };

        int count = hdmx.RecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!hdmx.TryGetDeviceRecord(i, out var record))
                continue;

            if (!record.TryGetWidths(numGlyphs, out var widths))
                continue;

            b._records.Add(new DeviceRecordEntry(record.PixelSize, record.MaxWidth, widths.ToArray()));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_records.Count > ushort.MaxValue)
            throw new InvalidOperationException("hdmx record count must fit in uint16.");

        _records.Sort(static (a, b) => a.PixelSize.CompareTo(b.PixelSize));

        int recordSize = Pad4(2 + _numGlyphs);
        if (recordSize < 2)
            throw new InvalidOperationException("Invalid hdmx record size.");

        int count = _records.Count;
        int length = checked(8 + (count * recordSize));

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, (ushort)count);
        BigEndian.WriteUInt32(span, 4, checked((uint)recordSize));

        int offset = 8;
        for (int i = 0; i < count; i++)
        {
            var r = _records[i];
            if (r.Widths.Length != _numGlyphs)
                throw new InvalidOperationException("hdmx device record widths length mismatch.");

            span[offset + 0] = r.PixelSize;
            span[offset + 1] = r.MaxWidth;
            r.Widths.AsSpan().CopyTo(span.Slice(offset + 2, _numGlyphs));
            offset += recordSize;
        }

        return table;
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    public readonly struct DeviceRecordEntry
    {
        public byte PixelSize { get; }
        public byte MaxWidth { get; }
        public byte[] Widths { get; }

        public DeviceRecordEntry(byte pixelSize, byte maxWidth, byte[] widths)
        {
            PixelSize = pixelSize;
            MaxWidth = maxWidth;
            Widths = widths;
        }
    }
}
