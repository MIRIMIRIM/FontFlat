namespace OTFontFile2;

public sealed class MemoryTableSource : ISfntTableSource
{
    private readonly Tag _tag;
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryTableSource(Tag tag, ReadOnlyMemory<byte> data)
    {
        _tag = tag;
        _data = data;
    }

    public Tag Tag => _tag;
    public int Length => _data.Length;

    public uint GetDirectoryChecksum()
    {
        var span = _data.Span;
        return _tag == SfntWriter.HeadTag
            ? OpenTypeChecksum.ComputeHeadDirectoryChecksum(span)
            : OpenTypeChecksum.Compute(span);
    }

    public void WriteTo(Stream destination, uint headCheckSumAdjustment)
    {
        if (_tag == SfntWriter.HeadTag && _data.Length >= 12)
        {
            var span = _data.Span;

            destination.Write(span.Slice(0, 8));

            Span<byte> adj = stackalloc byte[4];
            BigEndian.WriteUInt32(adj, 0, headCheckSumAdjustment);
            destination.Write(adj);

            destination.Write(span.Slice(12));
            return;
        }

        destination.Write(_data.Span);
    }
}

