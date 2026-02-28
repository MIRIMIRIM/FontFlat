namespace OTFontFile2;

public readonly struct TableSlice
{
    private readonly FontBuffer _buffer;

    internal TableSlice(FontBuffer buffer, Tag tag, uint directoryChecksum, int offset, int length)
    {
        _buffer = buffer;
        Tag = tag;
        DirectoryChecksum = directoryChecksum;
        Offset = offset;
        Length = length;
    }

    public Tag Tag { get; }
    public uint DirectoryChecksum { get; }
    public int Offset { get; }
    public int Length { get; }

    public ReadOnlySpan<byte> Span => _buffer.Slice(Offset, Length);

    public bool TryGetSpan(out ReadOnlySpan<byte> span) => _buffer.TrySlice(Offset, Length, out span);

    /// <summary>
    /// Creates a standalone <see cref="TableSlice"/> over the provided table bytes.
    /// This is useful when you already have raw table data (e.g. from Windows GDI <c>GetFontData</c> with a table tag).
    /// </summary>
    public static TableSlice CreateStandalone(Tag tag, ReadOnlyMemory<byte> tableBytes)
    {
        // Standalone slices always use offset=0 and wrap the provided bytes in a memory-backed FontBuffer.
        var buffer = FontBuffer.FromMemory(tableBytes);

        uint checksum = tag.Value == 0x68656164u // 'head'
            ? OpenTypeChecksum.ComputeHeadDirectoryChecksum(tableBytes.Span)
            : OpenTypeChecksum.Compute(tableBytes.Span);

        return new TableSlice(buffer, tag, checksum, offset: 0, length: tableBytes.Length);
    }

    /// <summary>
    /// Legacy try-style factory kept for call-site compatibility. This method currently never fails.
    /// </summary>
    public static bool TryCreateStandalone(Tag tag, ReadOnlyMemory<byte> tableBytes, out TableSlice slice)
    {
        slice = CreateStandalone(tag, tableBytes);
        return true;
    }
}
