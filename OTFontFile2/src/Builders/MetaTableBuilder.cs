using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>meta</c> table.
/// </summary>
[OtTableBuilder("meta")]
public sealed partial class MetaTableBuilder : ISfntTableSource
{
    private readonly List<Entry> _entries = new();

    private uint _version = 1;
    private uint _flags;

    public uint Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public uint Flags
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

    public int EntryCount => _entries.Count;

    public IReadOnlyList<Entry> Entries => _entries;

    public void Clear()
    {
        _entries.Clear();
        MarkDirty();
    }

    public void AddOrReplaceData(Tag tag, ReadOnlyMemory<byte> data)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Tag == tag)
                _entries.RemoveAt(i);
        }

        _entries.Add(new Entry(tag, data));
        MarkDirty();
    }

    public void AddOrReplaceUtf8String(Tag tag, string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        AddOrReplaceData(tag, Encoding.UTF8.GetBytes(value));
    }

    public bool Remove(Tag tag)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Tag == tag)
            {
                _entries.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public bool TryGetData(Tag tag, out ReadOnlyMemory<byte> data)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Tag == tag)
            {
                data = _entries[i].Data;
                return true;
            }
        }

        data = default;
        return false;
    }

    public static bool TryFrom(MetaTable meta, out MetaTableBuilder builder)
    {
        var b = new MetaTableBuilder
        {
            Version = meta.Version,
            Flags = meta.Flags
        };

        int count = (int)Math.Min(meta.DataMapCount, int.MaxValue);
        for (int i = 0; i < count; i++)
        {
            if (!meta.TryGetDataMap(i, out var map))
                continue;

            if (!meta.TryGetDataSpan(map, out var data))
                continue;

            b._entries.Add(new Entry(map.Tag, data.ToArray()));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        _entries.Sort(static (a, b) => a.Tag.CompareTo(b.Tag));

        int count = _entries.Count;
        int headerSize = checked(16 + (count * 12));
        int dataPos = headerSize;

        var dataOffsets = new uint[count];
        var dataLengths = new uint[count];

        for (int i = 0; i < count; i++)
        {
            int length = _entries[i].Data.Length;
            if (length < 0)
                throw new InvalidOperationException("Negative meta entry length.");

            dataOffsets[i] = checked((uint)dataPos);
            dataLengths[i] = checked((uint)length);

            dataPos = checked(dataPos + length);
            if (i != count - 1)
                dataPos = Pad4(dataPos);
        }

        byte[] table = new byte[dataPos];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version);
        BigEndian.WriteUInt32(span, 4, Flags);
        BigEndian.WriteUInt32(span, 8, checked((uint)headerSize));
        BigEndian.WriteUInt32(span, 12, checked((uint)count));

        int mapOffset = 16;
        for (int i = 0; i < count; i++)
        {
            var entry = _entries[i];
            BigEndian.WriteUInt32(span, mapOffset + 0, entry.Tag.Value);
            BigEndian.WriteUInt32(span, mapOffset + 4, dataOffsets[i]);
            BigEndian.WriteUInt32(span, mapOffset + 8, dataLengths[i]);
            mapOffset += 12;
        }

        for (int i = 0; i < count; i++)
        {
            var data = _entries[i].Data;
            int length = data.Length;
            if (length == 0)
                continue;

            int offset = checked((int)dataOffsets[i]);
            data.Span.CopyTo(span.Slice(offset, length));
        }

        return table;
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    public readonly struct Entry
    {
        public Tag Tag { get; }
        public ReadOnlyMemory<byte> Data { get; }

        public Entry(Tag tag, ReadOnlyMemory<byte> data)
        {
            Tag = tag;
            Data = data;
        }
    }
}
