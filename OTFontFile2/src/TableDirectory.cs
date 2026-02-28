namespace OTFontFile2;

public readonly struct TableDirectory
{
    private readonly FontBuffer _buffer;
    private readonly int _directoryOffset;
    private readonly ushort _count;

    internal TableDirectory(FontBuffer buffer, int directoryOffset, ushort count)
    {
        _buffer = buffer;
        _directoryOffset = directoryOffset;
        _count = count;
    }

    public ushort Count => _count;

    public TableRecord GetRecord(int index)
    {
        if ((uint)index >= _count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var data = _buffer.Span;
        int recordOffset = checked(_directoryOffset + (index * 16));
        return TableRecord.Read(data, recordOffset);
    }

    public bool TryFind(Tag tag, out TableRecord record)
    {
        var data = _buffer.Span;
        uint target = tag.Value;

        int lo = 0;
        int hi = _count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int recordOffset = _directoryOffset + (mid * 16);
            uint midTag = BigEndian.ReadUInt32(data, recordOffset);

            if (target < midTag)
            {
                hi = mid - 1;
                continue;
            }

            if (target > midTag)
            {
                lo = mid + 1;
                continue;
            }

            record = TableRecord.Read(data, recordOffset);
            return true;
        }

        // Fallback for malformed fonts that don't keep records sorted.
        for (int i = 0; i < _count; i++)
        {
            int recordOffset = _directoryOffset + (i * 16);
            uint t = BigEndian.ReadUInt32(data, recordOffset);
            if (t == target)
            {
                record = TableRecord.Read(data, recordOffset);
                return true;
            }
        }

        record = default;
        return false;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly TableDirectory _directory;
        private int _index;

        internal Enumerator(TableDirectory directory)
        {
            _directory = directory;
            _index = -1;
            Current = default;
        }

        public TableRecord Current { get; private set; }

        public bool MoveNext()
        {
            int next = _index + 1;
            if ((uint)next >= _directory._count)
                return false;

            _index = next;
            Current = _directory.GetRecord(next);
            return true;
        }
    }
}

public readonly struct TableRecord
{
    public Tag Tag { get; }
    public uint Checksum { get; }
    public uint Offset { get; }
    public uint Length { get; }

    public TableRecord(Tag tag, uint checksum, uint offset, uint length)
    {
        Tag = tag;
        Checksum = checksum;
        Offset = offset;
        Length = length;
    }

    internal static TableRecord Read(ReadOnlySpan<byte> data, int recordOffset)
    {
        uint tag = BigEndian.ReadUInt32(data, recordOffset);
        uint checksum = BigEndian.ReadUInt32(data, recordOffset + 4);
        uint offset = BigEndian.ReadUInt32(data, recordOffset + 8);
        uint length = BigEndian.ReadUInt32(data, recordOffset + 12);
        return new TableRecord(new Tag(tag), checksum, offset, length);
    }
}
