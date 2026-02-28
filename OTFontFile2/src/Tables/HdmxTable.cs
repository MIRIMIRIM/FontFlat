using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("hdmx", 8)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("RecordCount", OtFieldKind.UInt16, 2)]
[OtField("DeviceRecordSize", OtFieldKind.UInt32, 4)]
public readonly partial struct HdmxTable
{
    [OtSubTable(2, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("PixelSize", OtFieldKind.Byte, 0)]
    [OtField("MaxWidth", OtFieldKind.Byte, 1)]
    public readonly partial struct DeviceRecord
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _size;

        internal DeviceRecord(TableSlice hdmx, int offset, int size)
        {
            _table = hdmx;
            _offset = offset;
            _size = size;
        }

        public ReadOnlySpan<byte> WidthsAndPadding => _table.Span.Slice(_offset + 2, _size - 2);

        public bool TryGetWidths(int expectedGlyphCount, out ReadOnlySpan<byte> widths)
        {
            widths = default;

            if (expectedGlyphCount < 0)
                return false;

            if (_size < 2 || expectedGlyphCount > _size - 2)
                return false;

            widths = _table.Span.Slice(_offset + 2, expectedGlyphCount);
            return true;
        }
    }

    public bool TryGetDeviceRecord(int index, out DeviceRecord record)
    {
        record = default;

        ushort count = RecordCount;
        if ((uint)index >= (uint)count)
            return false;

        uint size = DeviceRecordSize;
        if (size > int.MaxValue || size < 2)
            return false;

        long offset = 8 + ((long)index * (long)size);
        if (offset < 0 || offset > _table.Length - (long)size)
            return false;

        record = new DeviceRecord(_table, (int)offset, (int)size);
        return true;
    }
}
