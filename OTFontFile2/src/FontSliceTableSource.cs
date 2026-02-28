namespace OTFontFile2;

/// <summary>
/// An <see cref="ISfntTableSource"/> that streams a table directly from an existing <see cref="FontBuffer"/> slice.
/// </summary>
public sealed class FontSliceTableSource : ISfntTableSource
{
    private readonly FontBuffer _buffer;
    private readonly Tag _tag;
    private readonly int _offset;
    private readonly int _length;

    private uint? _checksum;

    public FontSliceTableSource(FontBuffer buffer, Tag tag, int offset, int length)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _tag = tag;
        _offset = offset;
        _length = length;

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
    }

    public Tag Tag => _tag;
    public int Length => _length;

    public uint GetDirectoryChecksum()
    {
        if (_checksum is { } cached)
            return cached;

        if (!_buffer.TrySlice(_offset, _length, out var span))
            throw new InvalidDataException($"Table out of bounds: {_tag}.");

        uint checksum = _tag == SfntWriter.HeadTag
            ? OpenTypeChecksum.ComputeHeadDirectoryChecksum(span)
            : OpenTypeChecksum.Compute(span);

        _checksum = checksum;
        return checksum;
    }

    public void WriteTo(Stream destination, uint headCheckSumAdjustment)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        if (!_buffer.TrySlice(_offset, _length, out var span))
            throw new InvalidDataException($"Table out of bounds: {_tag}.");

        if (_tag == SfntWriter.HeadTag && span.Length >= 12)
        {
            destination.Write(span.Slice(0, 8));

            Span<byte> adj = stackalloc byte[4];
            BigEndian.WriteUInt32(adj, 0, headCheckSumAdjustment);
            destination.Write(adj);

            destination.Write(span.Slice(12));
            return;
        }

        destination.Write(span);
    }
}

