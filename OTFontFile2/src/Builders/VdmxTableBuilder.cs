using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>VDMX</c> table.
/// </summary>
[OtTableBuilder("VDMX")]
public sealed partial class VdmxTableBuilder : ISfntTableSource
{
    private readonly List<RatioRecord> _ratios = new();
    private readonly List<Group> _groups = new();
    private readonly List<int> _ratioGroupIndex = new();

    private ushort _version;

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

    public int RatioCount => _ratios.Count;
    public int GroupCount => _groups.Count;

    public IReadOnlyList<RatioRecord> Ratios => _ratios;
    public IReadOnlyList<Group> Groups => _groups;

    public void Clear()
    {
        _ratios.Clear();
        _groups.Clear();
        _ratioGroupIndex.Clear();
        MarkDirty();
    }

    public int AddGroup(byte startSize, byte endSize)
    {
        _groups.Add(new Group(startSize, endSize));
        MarkDirty();
        return _groups.Count - 1;
    }

    public void AddGroupEntry(int groupIndex, ushort yPelHeight, short yMax, short yMin)
    {
        if ((uint)groupIndex >= (uint)_groups.Count)
            throw new ArgumentOutOfRangeException(nameof(groupIndex));

        _groups[groupIndex].EntriesInternal.Add(new GroupEntry(yPelHeight, yMax, yMin));
        MarkDirty();
    }

    public void AddRatio(byte charSet, byte xRatio, byte yStartRatio, byte yEndRatio, int groupIndex)
    {
        if ((uint)groupIndex >= (uint)_groups.Count)
            throw new ArgumentOutOfRangeException(nameof(groupIndex));

        _ratios.Add(new RatioRecord(charSet, xRatio, yStartRatio, yEndRatio));
        _ratioGroupIndex.Add(groupIndex);
        MarkDirty();
    }

    public bool TryGetGroupIndexForRatio(int ratioIndex, out int groupIndex)
    {
        groupIndex = -1;
        if ((uint)ratioIndex >= (uint)_ratioGroupIndex.Count)
            return false;

        groupIndex = _ratioGroupIndex[ratioIndex];
        return true;
    }

    public static bool TryFrom(VdmxTable vdmx, out VdmxTableBuilder builder)
    {
        builder = null!;

        var b = new VdmxTableBuilder
        {
            Version = vdmx.Version
        };

        int ratioCount = vdmx.RatioCount;
        var groupIndexByOffset = new Dictionary<ushort, int>();

        for (int i = 0; i < ratioCount; i++)
        {
            if (!vdmx.TryGetRatio(i, out var ratio))
                continue;

            if (!vdmx.TryGetGroupOffsetForRatio(i, out ushort groupOffset))
                continue;

            if (!groupIndexByOffset.TryGetValue(groupOffset, out int groupIndex))
            {
                if (!vdmx.TryGetGroupForRatio(i, out var group))
                    continue;

                var newGroup = new Group(group.StartSize, group.EndSize);
                int entryCount = group.EntryCount;
                for (int e = 0; e < entryCount; e++)
                {
                    if (!group.TryGetEntry(e, out var entry))
                        continue;

                    newGroup.EntriesInternal.Add(new GroupEntry(entry.YPelHeight, entry.YMax, entry.YMin));
                }

                b._groups.Add(newGroup);
                groupIndex = b._groups.Count - 1;
                groupIndexByOffset.Add(groupOffset, groupIndex);
            }

            b._ratios.Add(new RatioRecord(ratio.CharSet, ratio.XRatio, ratio.YStartRatio, ratio.YEndRatio));
            b._ratioGroupIndex.Add(groupIndex);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_ratios.Count != _ratioGroupIndex.Count)
            throw new InvalidOperationException("VDMX internal ratio/group mapping is inconsistent.");

        if (_ratios.Count > ushort.MaxValue)
            throw new InvalidOperationException("VDMX ratio count must fit in uint16.");

        if (_groups.Count > ushort.MaxValue)
            throw new InvalidOperationException("VDMX group count must fit in uint16.");

        int ratioCount = _ratios.Count;
        int groupCount = _groups.Count;

        int headerSize = checked(6 + (ratioCount * 4) + (ratioCount * 2));
        int dataPos = headerSize;

        var groupOffsets = new ushort[groupCount];

        for (int g = 0; g < groupCount; g++)
        {
            if (dataPos > ushort.MaxValue)
                throw new InvalidOperationException("VDMX group offset must fit in uint16.");

            groupOffsets[g] = (ushort)dataPos;

            var group = _groups[g];
            int entryCount = group.Entries.Count;
            if (entryCount > ushort.MaxValue)
                throw new InvalidOperationException("VDMX group entry count must fit in uint16.");

            int groupLen = checked(4 + (entryCount * 6));
            dataPos = checked(dataPos + groupLen);
        }

        byte[] table = new byte[dataPos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, (ushort)groupCount);
        BigEndian.WriteUInt16(span, 4, (ushort)ratioCount);

        int ratioOffset = 6;
        for (int i = 0; i < ratioCount; i++)
        {
            var r = _ratios[i];
            span[ratioOffset + 0] = r.CharSet;
            span[ratioOffset + 1] = r.XRatio;
            span[ratioOffset + 2] = r.YStartRatio;
            span[ratioOffset + 3] = r.YEndRatio;
            ratioOffset += 4;
        }

        int offsetsOffset = 6 + (ratioCount * 4);
        for (int i = 0; i < ratioCount; i++)
        {
            int groupIndex = _ratioGroupIndex[i];
            if ((uint)groupIndex >= (uint)groupCount)
                throw new InvalidOperationException("VDMX ratio references an invalid group index.");

            BigEndian.WriteUInt16(span, offsetsOffset + (i * 2), groupOffsets[groupIndex]);
        }

        for (int g = 0; g < groupCount; g++)
        {
            var group = _groups[g];
            int groupStart = groupOffsets[g];

            int entryCount = group.Entries.Count;
            BigEndian.WriteUInt16(span, groupStart + 0, (ushort)entryCount);
            span[groupStart + 2] = group.StartSize;
            span[groupStart + 3] = group.EndSize;

            int entryOffset = groupStart + 4;
            for (int e = 0; e < entryCount; e++)
            {
                var entry = group.Entries[e];
                BigEndian.WriteUInt16(span, entryOffset + 0, entry.YPelHeight);
                BigEndian.WriteInt16(span, entryOffset + 2, entry.YMax);
                BigEndian.WriteInt16(span, entryOffset + 4, entry.YMin);
                entryOffset += 6;
            }
        }

        return table;
    }

    public readonly struct RatioRecord
    {
        public byte CharSet { get; }
        public byte XRatio { get; }
        public byte YStartRatio { get; }
        public byte YEndRatio { get; }

        public RatioRecord(byte charSet, byte xRatio, byte yStartRatio, byte yEndRatio)
        {
            CharSet = charSet;
            XRatio = xRatio;
            YStartRatio = yStartRatio;
            YEndRatio = yEndRatio;
        }
    }

    public sealed class Group
    {
        private readonly List<GroupEntry> _entries = new();

        public byte StartSize { get; }
        public byte EndSize { get; }
        public IReadOnlyList<GroupEntry> Entries => _entries;

        internal List<GroupEntry> EntriesInternal => _entries;

        public Group(byte startSize, byte endSize)
        {
            StartSize = startSize;
            EndSize = endSize;
        }
    }

    public readonly struct GroupEntry
    {
        public ushort YPelHeight { get; }
        public short YMax { get; }
        public short YMin { get; }

        public GroupEntry(ushort yPelHeight, short yMax, short yMin)
        {
            YPelHeight = yPelHeight;
            YMax = yMax;
            YMin = yMin;
        }
    }
}
