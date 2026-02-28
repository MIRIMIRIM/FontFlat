using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the <c>GMAP</c> table.
/// </summary>
[OtTableBuilder("GMAP")]
public sealed partial class GmapTableBuilder : ISfntTableSource
{
    private const int HeaderSize = 12;
    private const int RecordSize = 42;
    private const int RecordNameBytesLength = 32;

    private readonly List<RecordEntry> _records = new();

    private ushort _tableVersionMajor;
    private ushort _tableVersionMinor;
    private ushort _flags;
    private ReadOnlyMemory<byte> _psFontName;

    public ushort TableVersionMajor
    {
        get => _tableVersionMajor;
        set
        {
            if (value == _tableVersionMajor)
                return;
            _tableVersionMajor = value;
            MarkDirty();
        }
    }

    public ushort TableVersionMinor
    {
        get => _tableVersionMinor;
        set
        {
            if (value == _tableVersionMinor)
                return;
            _tableVersionMinor = value;
            MarkDirty();
        }
    }

    public ushort Flags
    {
        get => _flags;
        set
        {
            if (value == _flags)
                return;
            _flags = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> PsFontNameBytes => _psFontName;

    public void SetPsFontNameBytes(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(bytes), "PS font name length must fit in uint16.");

        _psFontName = bytes;
        MarkDirty();
    }

    public void SetPsFontNameString(string ascii)
    {
        if (ascii is null) throw new ArgumentNullException(nameof(ascii));
        SetPsFontNameBytes(Encoding.ASCII.GetBytes(ascii));
    }

    public int RecordCount => _records.Count;

    public IReadOnlyList<RecordEntry> Records => _records;

    public void Clear()
    {
        _records.Clear();
        _psFontName = ReadOnlyMemory<byte>.Empty;
        _tableVersionMajor = 0;
        _tableVersionMinor = 0;
        _flags = 0;
        MarkDirty();
    }

    public void AddRecord(uint unicodeValue, ushort cid, ushort gid, ushort glyphletGid, string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > RecordNameBytesLength)
            throw new ArgumentOutOfRangeException(nameof(name), $"Name must be <= {RecordNameBytesLength} ASCII bytes.");

        _records.Add(new RecordEntry(unicodeValue, cid, gid, glyphletGid, nameBytes));
        MarkDirty();
    }

    public static bool TryFrom(GmapTable gmap, out GmapTableBuilder builder)
    {
        builder = null!;

        var b = new GmapTableBuilder
        {
            TableVersionMajor = gmap.TableVersionMajor,
            TableVersionMinor = gmap.TableVersionMinor,
            Flags = gmap.Flags
        };

        if (gmap.TryGetPsFontNameBytes(out var ps))
            b._psFontName = ps.ToArray();

        int count = gmap.RecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!gmap.TryGetRecord(i, out var r))
                continue;

            byte[] nameBytes = r.NameBytes.ToArray();
            b._records.Add(new RecordEntry(r.UnicodeValue, r.Cid, r.Gid, r.GlyphletGid, nameBytes));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_records.Count > ushort.MaxValue)
            throw new InvalidOperationException("GMAP record count must fit in uint16.");

        int fontNameLength = _psFontName.Length;
        if (fontNameLength > ushort.MaxValue)
            throw new InvalidOperationException("GMAP PS font name length must fit in uint16.");

        int recordsOffset = Pad4(checked(HeaderSize + fontNameLength));

        int length = checked(recordsOffset + (_records.Count * RecordSize));
        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, TableVersionMajor);
        BigEndian.WriteUInt16(span, 2, TableVersionMinor);
        BigEndian.WriteUInt16(span, 4, Flags);
        BigEndian.WriteUInt16(span, 6, checked((ushort)_records.Count));
        BigEndian.WriteUInt16(span, 8, checked((ushort)recordsOffset));
        BigEndian.WriteUInt16(span, 10, checked((ushort)fontNameLength));

        if (fontNameLength != 0)
            _psFontName.Span.CopyTo(span.Slice(HeaderSize, fontNameLength));

        int pos = recordsOffset;
        for (int i = 0; i < _records.Count; i++)
        {
            var r = _records[i];

            BigEndian.WriteUInt32(span, pos + 0, r.UnicodeValue);
            BigEndian.WriteUInt16(span, pos + 4, r.Cid);
            BigEndian.WriteUInt16(span, pos + 6, r.Gid);
            BigEndian.WriteUInt16(span, pos + 8, r.GlyphletGid);

            if (r.NameBytes.Length > RecordNameBytesLength)
                throw new InvalidOperationException("GMAP record name exceeds 32 bytes.");

            var nameDest = span.Slice(pos + 10, RecordNameBytesLength);
            nameDest.Clear();
            r.NameBytes.AsSpan().CopyTo(nameDest);

            pos += RecordSize;
        }

        return table;
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    public readonly struct RecordEntry
    {
        public uint UnicodeValue { get; }
        public ushort Cid { get; }
        public ushort Gid { get; }
        public ushort GlyphletGid { get; }
        public byte[] NameBytes { get; }

        public RecordEntry(uint unicodeValue, ushort cid, ushort gid, ushort glyphletGid, byte[] nameBytes)
        {
            UnicodeValue = unicodeValue;
            Cid = cid;
            Gid = gid;
            GlyphletGid = glyphletGid;
            NameBytes = nameBytes;
        }
    }
}

