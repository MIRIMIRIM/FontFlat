using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Adobe Glyphlets mapping table (<c>GMAP</c>).
/// </summary>
[OtTable("GMAP", 12)]
[OtField("TableVersionMajor", OtFieldKind.UInt16, 0)]
[OtField("TableVersionMinor", OtFieldKind.UInt16, 2)]
[OtField("Flags", OtFieldKind.UInt16, 4)]
[OtField("RecordCount", OtFieldKind.UInt16, 6)]
[OtField("RecordsOffset", OtFieldKind.UInt16, 8)]
[OtField("FontNameLength", OtFieldKind.UInt16, 10)]
public readonly partial struct GmapTable
{
    private const int HeaderSize = 12;
    private const int RecordSize = 42;
    private const int RecordNameBytesLength = 32;

    public bool TryGetPsFontNameBytes(out ReadOnlySpan<byte> bytes)
    {
        bytes = default;

        int length = FontNameLength;
        if (length == 0)
        {
            bytes = ReadOnlySpan<byte>.Empty;
            return true;
        }

        if ((uint)HeaderSize > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - HeaderSize))
            return false;

        bytes = _table.Span.Slice(HeaderSize, length);
        return true;
    }

    public bool TryGetPsFontNameString(out string name)
    {
        name = "";

        if (!TryGetPsFontNameBytes(out var bytes))
            return false;

        name = Encoding.ASCII.GetString(bytes);
        return true;
    }

    public bool TryGetRecord(int index, out GmapRecord record)
    {
        record = default;

        ushort count = RecordCount;
        if ((uint)index >= (uint)count)
            return false;

        int baseOffset = RecordsOffset;
        if ((uint)baseOffset > (uint)_table.Length)
            return false;

        int offset = checked(baseOffset + (index * RecordSize));
        if ((uint)offset > (uint)_table.Length - RecordSize)
            return false;

        record = new GmapRecord(_table, offset);
        return true;
    }

    public readonly struct GmapRecord
    {
        private readonly TableSlice _table;
        private readonly int _offset;

        internal GmapRecord(TableSlice table, int offset)
        {
            _table = table;
            _offset = offset;
        }

        public uint UnicodeValue => BigEndian.ReadUInt32(_table.Span, _offset);
        public ushort Cid => BigEndian.ReadUInt16(_table.Span, _offset + 4);
        public ushort Gid => BigEndian.ReadUInt16(_table.Span, _offset + 6);
        public ushort GlyphletGid => BigEndian.ReadUInt16(_table.Span, _offset + 8);

        public ReadOnlySpan<byte> NameBytes => _table.Span.Slice(_offset + 10, RecordNameBytesLength);

        public string GetNameString()
        {
            var bytes = NameBytes;
            int len = bytes.IndexOf((byte)0);
            if (len < 0)
                len = bytes.Length;
            return Encoding.ASCII.GetString(bytes.Slice(0, len));
        }
    }
}

