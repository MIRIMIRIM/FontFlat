using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("meta", 16)]
[OtField("Version", OtFieldKind.UInt32, 0)]
[OtField("Flags", OtFieldKind.UInt32, 4)]
[OtField("DataOffset", OtFieldKind.UInt32, 8)]
[OtField("DataMapCount", OtFieldKind.UInt32, 12)]
[OtSequentialRecordArray("DataMap", 16, 12)]
public readonly partial struct MetaTable
{
    public readonly struct DataMap
    {
        public Tag Tag { get; }
        public uint DataOffset { get; }
        public uint DataLength { get; }

        public DataMap(Tag tag, uint dataOffset, uint dataLength)
        {
            Tag = tag;
            DataOffset = dataOffset;
            DataLength = dataLength;
        }
    }

    public bool TryFindDataMap(Tag tag, out DataMap map)
    {
        int count = (int)Math.Min(DataMapCount, int.MaxValue);
        for (int i = 0; i < count; i++)
        {
            if (!TryGetDataMap(i, out var candidate))
                continue;

            if (candidate.Tag == tag)
            {
                map = candidate;
                return true;
            }
        }

        map = default;
        return false;
    }

    public bool TryGetDataSpan(DataMap map, out ReadOnlySpan<byte> data)
    {
        data = default;

        if (map.DataOffset > int.MaxValue || map.DataLength > int.MaxValue)
            return false;

        int offset = (int)map.DataOffset;
        int length = (int)map.DataLength;
        if ((uint)offset > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - offset))
            return false;

        data = _table.Span.Slice(offset, length);
        return true;
    }

    public bool TryGetDataSpan(int index, out ReadOnlySpan<byte> data)
    {
        data = default;
        return TryGetDataMap(index, out var map) && TryGetDataSpan(map, out data);
    }

    public string? GetUtf8String(int index)
    {
        if (!TryGetDataSpan(index, out var data))
            return null;

        return Encoding.UTF8.GetString(data);
    }
}
