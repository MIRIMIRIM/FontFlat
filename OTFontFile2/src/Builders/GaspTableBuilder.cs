using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>gasp</c> table.
/// </summary>
[OtTableBuilder("gasp")]
public sealed partial class GaspTableBuilder : ISfntTableSource
{
    private readonly List<GaspRangeEntry> _ranges = new();

    private ushort _version = 1;

    public ushort Version
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

    public int RangeCount => _ranges.Count;

    public IReadOnlyList<GaspRangeEntry> Ranges => _ranges;

    public void Clear()
    {
        _ranges.Clear();
        MarkDirty();
    }

    public void AddOrReplaceRange(ushort rangeMaxPpem, GaspTable.GaspBehavior behavior)
    {
        for (int i = _ranges.Count - 1; i >= 0; i--)
        {
            if (_ranges[i].RangeMaxPpem == rangeMaxPpem)
                _ranges.RemoveAt(i);
        }

        _ranges.Add(new GaspRangeEntry(rangeMaxPpem, behavior));
        MarkDirty();
    }

    public bool RemoveRange(ushort rangeMaxPpem)
    {
        bool removed = false;
        for (int i = _ranges.Count - 1; i >= 0; i--)
        {
            if (_ranges[i].RangeMaxPpem == rangeMaxPpem)
            {
                _ranges.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public static bool TryFrom(GaspTable gasp, out GaspTableBuilder builder)
    {
        var b = new GaspTableBuilder { Version = gasp.Version };

        int count = gasp.RangeCount;
        for (int i = 0; i < count; i++)
        {
            if (!gasp.TryGetRange(i, out var range))
                continue;

            b._ranges.Add(new GaspRangeEntry(range.RangeMaxPpem, range.Behavior));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_ranges.Count > ushort.MaxValue)
            throw new InvalidOperationException("gasp range count must fit in uint16.");

        _ranges.Sort(static (a, b) => a.RangeMaxPpem.CompareTo(b.RangeMaxPpem));

        int count = _ranges.Count;
        int length = checked(4 + (count * 4));
        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, (ushort)count);

        int offset = 4;
        for (int i = 0; i < count; i++)
        {
            var r = _ranges[i];
            BigEndian.WriteUInt16(span, offset, r.RangeMaxPpem);
            BigEndian.WriteUInt16(span, offset + 2, (ushort)r.Behavior);
            offset += 4;
        }

        return table;
    }

    public readonly struct GaspRangeEntry
    {
        public ushort RangeMaxPpem { get; }
        public GaspTable.GaspBehavior Behavior { get; }

        public GaspRangeEntry(ushort rangeMaxPpem, GaspTable.GaspBehavior behavior)
        {
            RangeMaxPpem = rangeMaxPpem;
            Behavior = behavior;
        }
    }
}
