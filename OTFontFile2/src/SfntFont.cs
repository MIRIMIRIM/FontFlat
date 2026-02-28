using System.Buffers.Binary;

namespace OTFontFile2;

public readonly struct SfntFont
{
    private readonly FontBuffer _buffer;
    private readonly int _offsetTableOffset;
    private readonly uint _sfntVersion;
    private readonly ushort _numTables;

    private SfntFont(FontBuffer buffer, int offsetTableOffset, uint sfntVersion, ushort numTables)
    {
        _buffer = buffer;
        _offsetTableOffset = offsetTableOffset;
        _sfntVersion = sfntVersion;
        _numTables = numTables;
    }

    public uint SfntVersion => _sfntVersion;
    public ushort TableCount => _numTables;

    public TableDirectory Directory => new(_buffer, checked(_offsetTableOffset + 12), _numTables);

    internal FontBuffer Buffer => _buffer;

    public static bool TryCreate(FontBuffer buffer, int offsetTableOffset, out SfntFont font, out FontParseError error)
    {
        font = default;

        var data = buffer.Span;
        if ((uint)offsetTableOffset > (uint)data.Length || data.Length - offsetTableOffset < 12)
        {
            error = new FontParseError(FontParseErrorKind.InvalidOffsetTable, offset: offsetTableOffset);
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offsetTableOffset, 4));
        if (!IsValidSfntVersion(version))
        {
            error = new FontParseError(FontParseErrorKind.InvalidSfntVersion, offset: offsetTableOffset);
            return false;
        }

        ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offsetTableOffset + 4, 2));
        int directoryOffset = offsetTableOffset + 12;
        long directoryBytes = (long)numTables * 16;
        if (numTables == 0 || directoryOffset < 0 || directoryOffset + directoryBytes > data.Length)
        {
            error = new FontParseError(FontParseErrorKind.InvalidTableDirectory, offset: directoryOffset);
            return false;
        }

        font = new SfntFont(buffer, offsetTableOffset, version, numTables);
        error = default;
        return true;
    }

    public bool TryGetTable(Tag tag, out TableRecord record)
        => Directory.TryFind(tag, out record);

    public bool TryGetTableData(Tag tag, out ReadOnlySpan<byte> tableData, out TableRecord record)
    {
        tableData = default;
        record = default;

        if (!TryGetTable(tag, out record))
            return false;

        if (!_buffer.TrySlice((int)record.Offset, (int)record.Length, out tableData))
            return false;

        return true;
    }

    public bool TryGetTableSlice(Tag tag, out TableSlice table)
    {
        table = default;

        if (!TryGetTable(tag, out var record))
            return false;

        if (record.Offset > int.MaxValue || record.Length > int.MaxValue)
            return false;

        int offset = (int)record.Offset;
        int length = (int)record.Length;

        if (!_buffer.TrySlice(offset, length, out _))
            return false;

        table = new TableSlice(_buffer, record.Tag, record.Checksum, offset, length);
        return true;
    }

    private static bool IsValidSfntVersion(uint value)
    {
        return value == 0x00010000
            || value == 0x4F54544F // 'OTTO'
            || value == 0x74727565 // 'true'
            || value == 0x74797031; // 'typ1'
    }
}
