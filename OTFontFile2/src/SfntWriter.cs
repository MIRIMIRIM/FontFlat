namespace OTFontFile2;

public static class SfntWriter
{
    public static readonly Tag HeadTag = new(0x68656164); // 'head'

    private const uint ChecksumAdjustmentMagic = 0xB1B0AFBA;

    public static void Write(Stream destination, SfntFont font, SfntWriteOptions? options = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        options ??= new SfntWriteOptions();

        int count = font.TableCount;
        if (count == 0)
            throw new InvalidOperationException("Font has no tables.");

        // Materialize table records.
        var records = new TableRecord[count];
        for (int i = 0; i < count; i++)
            records[i] = font.Directory.GetRecord(i);

        if (options.TableOrdering == SfntTableOrdering.ByTagAscending)
        {
            Array.Sort(records, static (a, b) => a.Tag.CompareTo(b.Tag));
        }

        var layout = new TableLayoutEntry[count];
        var buffer = font.Buffer;

        int headerSize = checked(12 + (count * 16));
        int tableDataOffset = headerSize;

        bool hasHead = false;
        unchecked
        {
            uint tablesChecksumSum = 0;
            for (int i = 0; i < count; i++)
            {
                var r = records[i];

                if (!buffer.TrySlice((int)r.Offset, (int)r.Length, out var tableData))
                    throw new InvalidDataException($"Table out of bounds: {r.Tag}.");

                uint checksum = r.Tag == HeadTag
                    ? OpenTypeChecksum.ComputeHeadDirectoryChecksum(tableData)
                    : OpenTypeChecksum.Compute(tableData);

                layout[i] = new TableLayoutEntry(
                    tag: r.Tag,
                    offset: (uint)tableDataOffset,
                    length: (uint)r.Length,
                    directoryChecksum: checksum,
                    source: TableWriteSource.FromFontSlice(buffer, (int)r.Offset, (int)r.Length));

                if (r.Tag == HeadTag)
                    hasHead = true;

                tablesChecksumSum += checksum;
                tableDataOffset = checked(tableDataOffset + Pad4((int)r.Length));
            }

            uint headAdjustment = 0;
            if (hasHead && options.WriteHeadCheckSumAdjustment)
            {
                uint headerChecksum = ComputeHeaderChecksum(font.SfntVersion, layout);
                uint baseSum = headerChecksum + tablesChecksumSum;
                headAdjustment = ChecksumAdjustmentMagic - baseSum;
            }

            WriteInternal(destination, font.SfntVersion, layout, headAdjustment);
        }
    }

    public static void Write(Stream destination, uint sfntVersion, IEnumerable<ISfntTableSource> tables, SfntWriteOptions? options = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (tables is null) throw new ArgumentNullException(nameof(tables));
        options ??= new SfntWriteOptions();

        // Materialize sources (needed for directory + layout).
        var sources = tables as ICollection<ISfntTableSource>;
        List<ISfntTableSource> list = sources is null ? new List<ISfntTableSource>() : new List<ISfntTableSource>(sources.Count);

        var seen = new HashSet<Tag>();
        foreach (var table in tables)
        {
            if (!seen.Add(table.Tag))
                throw new ArgumentException($"Duplicate table tag: {table.Tag}.", nameof(tables));
            if (table.Length < 0)
                throw new ArgumentException($"Negative table length: {table.Tag}.", nameof(tables));
            list.Add(table);
        }

        if (list.Count == 0)
            throw new ArgumentException("At least one table is required.", nameof(tables));

        if (options.TableOrdering == SfntTableOrdering.ByTagAscending)
        {
            list.Sort(static (a, b) => a.Tag.CompareTo(b.Tag));
        }

        int count = list.Count;
        var layout = new TableLayoutEntry[count];

        int headerSize = checked(12 + (count * 16));
        int tableDataOffset = headerSize;

        bool hasHead = false;
        unchecked
        {
            uint tablesChecksumSum = 0;
            for (int i = 0; i < count; i++)
            {
                var source = list[i];
                uint checksum = source.GetDirectoryChecksum();

                layout[i] = new TableLayoutEntry(
                    tag: source.Tag,
                    offset: (uint)tableDataOffset,
                    length: (uint)source.Length,
                    directoryChecksum: checksum,
                    source: TableWriteSource.FromTableSource(source));

                if (source.Tag == HeadTag)
                    hasHead = true;

                tablesChecksumSum += checksum;
                tableDataOffset = checked(tableDataOffset + Pad4(source.Length));
            }

            uint headAdjustment = 0;
            if (hasHead && options.WriteHeadCheckSumAdjustment)
            {
                uint headerChecksum = ComputeHeaderChecksum(sfntVersion, layout);
                uint baseSum = headerChecksum + tablesChecksumSum;
                headAdjustment = ChecksumAdjustmentMagic - baseSum;
            }

            WriteInternal(destination, sfntVersion, layout, headAdjustment);
        }
    }

    private static void WriteInternal(Stream destination, uint sfntVersion, ReadOnlySpan<TableLayoutEntry> layout, uint headAdjustment)
    {
        byte[] header = BuildHeader(sfntVersion, layout);
        destination.Write(header);

        for (int i = 0; i < layout.Length; i++)
        {
            ref readonly var entry = ref layout[i];

            entry.Source.WriteTo(destination, isHead: entry.Tag == HeadTag, headCheckSumAdjustment: headAdjustment);

            int pad = Pad4((int)entry.Length) - (int)entry.Length;
            if (pad != 0)
                WriteZeros(destination, pad);
        }
    }

    private static byte[] BuildHeader(uint sfntVersion, ReadOnlySpan<TableLayoutEntry> layout)
    {
        int count = layout.Length;
        byte[] header = new byte[checked(12 + (count * 16))];
        var span = header.AsSpan();

        BigEndian.WriteUInt32(span, 0, sfntVersion);
        BigEndian.WriteUInt16(span, 4, (ushort)count);

        ComputeSearchFields((ushort)count, out ushort searchRange, out ushort entrySelector, out ushort rangeShift);
        BigEndian.WriteUInt16(span, 6, searchRange);
        BigEndian.WriteUInt16(span, 8, entrySelector);
        BigEndian.WriteUInt16(span, 10, rangeShift);

        int dirOffset = 12;
        for (int i = 0; i < count; i++)
        {
            ref readonly var e = ref layout[i];
            int recordOffset = dirOffset + (i * 16);

            BigEndian.WriteUInt32(span, recordOffset + 0, e.Tag.Value);
            BigEndian.WriteUInt32(span, recordOffset + 4, e.DirectoryChecksum);
            BigEndian.WriteUInt32(span, recordOffset + 8, e.Offset);
            BigEndian.WriteUInt32(span, recordOffset + 12, e.Length);
        }

        return header;
    }

    private static uint ComputeHeaderChecksum(uint sfntVersion, ReadOnlySpan<TableLayoutEntry> layout)
    {
        byte[] header = BuildHeader(sfntVersion, layout);
        return OpenTypeChecksum.Compute(header);
    }

    private static void WriteZeros(Stream destination, int count)
    {
        if (count <= 0)
            return;
        if ((uint)count > 3)
            throw new ArgumentOutOfRangeException(nameof(count));

        // Padding written by this writer is always 1..3 bytes.
        Span<byte> zeros = stackalloc byte[3];
        destination.Write(zeros.Slice(0, count));
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    private static void ComputeSearchFields(ushort numTables, out ushort searchRange, out ushort entrySelector, out ushort rangeShift)
    {
        // Per OpenType spec.
        ushort maxPower2 = 1;
        while ((ushort)(maxPower2 << 1) != 0 && (ushort)(maxPower2 << 1) <= numTables)
            maxPower2 <<= 1;

        ushort log2 = 0;
        ushort tmp = maxPower2;
        while (tmp > 1)
        {
            tmp >>= 1;
            log2++;
        }

        searchRange = (ushort)(maxPower2 * 16);
        entrySelector = log2;
        rangeShift = (ushort)((numTables * 16) - searchRange);
    }

    private readonly struct TableLayoutEntry
    {
        public readonly Tag Tag;
        public readonly uint Offset;
        public readonly uint Length;
        public readonly uint DirectoryChecksum;
        public readonly TableWriteSource Source;

        public TableLayoutEntry(Tag tag, uint offset, uint length, uint directoryChecksum, TableWriteSource source)
        {
            Tag = tag;
            Offset = offset;
            Length = length;
            DirectoryChecksum = directoryChecksum;
            Source = source;
        }
    }

    private readonly struct TableWriteSource
    {
        private readonly ISfntTableSource? _table;
        private readonly FontBuffer? _buffer;
        private readonly int _offset;
        private readonly int _length;

        private TableWriteSource(ISfntTableSource table)
        {
            _table = table;
            _buffer = null;
            _offset = 0;
            _length = 0;
        }

        private TableWriteSource(FontBuffer buffer, int offset, int length)
        {
            _table = null;
            _buffer = buffer;
            _offset = offset;
            _length = length;
        }

        public static TableWriteSource FromTableSource(ISfntTableSource table) => new(table);
        public static TableWriteSource FromFontSlice(FontBuffer buffer, int offset, int length) => new(buffer, offset, length);

        public void WriteTo(Stream destination, bool isHead, uint headCheckSumAdjustment)
        {
            if (_table is not null)
            {
                _table.WriteTo(destination, isHead ? headCheckSumAdjustment : 0);
                return;
            }

            var buffer = _buffer!;
            if (!buffer.TrySlice(_offset, _length, out var tableData))
                throw new InvalidDataException("Table out of bounds.");

            if (isHead && tableData.Length >= 12)
            {
                destination.Write(tableData.Slice(0, 8));

                Span<byte> adj = stackalloc byte[4];
                BigEndian.WriteUInt32(adj, 0, headCheckSumAdjustment);
                destination.Write(adj);

                destination.Write(tableData.Slice(12));
                return;
            }

            destination.Write(tableData);
        }
    }
}
