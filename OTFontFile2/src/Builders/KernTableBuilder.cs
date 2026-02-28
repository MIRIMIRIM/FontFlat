using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>kern</c> table (v0, format 0 only).
/// </summary>
[OtTableBuilder("kern")]
public sealed partial class KernTableBuilder : ISfntTableSource
{
    private readonly List<Format0SubtableBuilder> _subtables = new();

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

    public int SubtableCount => _subtables.Count;

    public IReadOnlyList<Format0SubtableBuilder> Subtables => _subtables;

    public void Clear()
    {
        _subtables.Clear();
        MarkDirty();
    }

    public Format0SubtableBuilder AddFormat0Subtable(
        bool horizontal = true,
        bool minimum = false,
        bool crossStream = false,
        bool @override = false)
    {
        ushort flags = 0;
        if (horizontal) flags |= 0x0001;
        if (minimum) flags |= 0x0002;
        if (crossStream) flags |= 0x0004;
        if (@override) flags |= 0x0008;

        var st = new Format0SubtableBuilder(this, flags);
        _subtables.Add(st);
        MarkDirty();
        return st;
    }

    public static bool TryFrom(KernTable kern, out KernTableBuilder builder)
    {
        builder = null!;

        // Only v0 is supported by the current view.
        if (kern.Version != 0)
            return false;

        var b = new KernTableBuilder
        {
            Version = kern.Version
        };

        int count = kern.SubtableCount;
        for (int i = 0; i < count; i++)
        {
            if (!kern.TryGetSubtable(i, out var st))
                continue;

            if (!st.TryGetFormat0(out var fmt0))
                continue;

            ushort flags = (ushort)(st.Coverage & 0x00FF);
            var sub = new Format0SubtableBuilder(b, flags);

            int pairCount = fmt0.PairCount;
            for (int p = 0; p < pairCount; p++)
            {
                if (!fmt0.TryGetPair(p, out var pair))
                    continue;

                sub.AddPairUnchecked(pair.Left, pair.Right, pair.Value);
            }

            b._subtables.Add(sub);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_subtables.Count > ushort.MaxValue)
            throw new InvalidOperationException("kern subtable count must fit in uint16.");

        int count = _subtables.Count;

        int totalLength = 4;
        for (int i = 0; i < count; i++)
        {
            var st = _subtables[i];
            int pairCount = st.PairCount;
            int subLen = checked(14 + (pairCount * 6));
            if (subLen > ushort.MaxValue)
                throw new InvalidOperationException("kern subtable length must fit in uint16.");

            totalLength = checked(totalLength + subLen);
        }

        byte[] table = new byte[totalLength];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, (ushort)count);

        int offset = 4;
        for (int i = 0; i < count; i++)
        {
            var st = _subtables[i];
            st.SortPairs();

            int pairCount = st.PairCount;
            ushort length = checked((ushort)(14 + (pairCount * 6)));

            BigEndian.WriteUInt16(span, offset + 0, 0); // subtable version
            BigEndian.WriteUInt16(span, offset + 2, length);
            BigEndian.WriteUInt16(span, offset + 4, st.Coverage);

            BigEndian.WriteUInt16(span, offset + 6, checked((ushort)pairCount));

            ComputeSearchFields(pairCount, out ushort searchRange, out ushort entrySelector, out ushort rangeShift);
            BigEndian.WriteUInt16(span, offset + 8, searchRange);
            BigEndian.WriteUInt16(span, offset + 10, entrySelector);
            BigEndian.WriteUInt16(span, offset + 12, rangeShift);

            int pairOffset = offset + 14;
            for (int p = 0; p < pairCount; p++)
            {
                var pair = st.Pairs[p];
                BigEndian.WriteUInt16(span, pairOffset + 0, pair.Left);
                BigEndian.WriteUInt16(span, pairOffset + 2, pair.Right);
                BigEndian.WriteInt16(span, pairOffset + 4, pair.Value);
                pairOffset += 6;
            }

            offset += length;
        }

        return table;
    }

    private static void ComputeSearchFields(int numPairs, out ushort searchRange, out ushort entrySelector, out ushort rangeShift)
    {
        if (numPairs < 0)
            throw new ArgumentOutOfRangeException(nameof(numPairs));

        ushort maxPower2 = 1;
        while ((ushort)(maxPower2 << 1) != 0 && (ushort)(maxPower2 << 1) <= numPairs)
            maxPower2 <<= 1;

        ushort log2 = 0;
        ushort tmp = maxPower2;
        while (tmp > 1)
        {
            tmp >>= 1;
            log2++;
        }

        searchRange = (ushort)(maxPower2 * 6);
        entrySelector = log2;
        rangeShift = (ushort)((numPairs * 6) - searchRange);
    }

    public sealed class Format0SubtableBuilder
    {
        private readonly KernTableBuilder _owner;
        private readonly List<KerningPair> _pairs = new();

        private ushort _coverageFlags;

        internal Format0SubtableBuilder(KernTableBuilder owner, ushort coverageFlags)
        {
            _owner = owner;
            _coverageFlags = (ushort)(coverageFlags & 0x00FF);
        }

        public ushort CoverageFlags
        {
            get => _coverageFlags;
            set
            {
                _coverageFlags = (ushort)(value & 0x00FF);
                _owner.MarkDirty();
            }
        }

        public ushort Coverage => _coverageFlags; // format 0 in high byte

        public int PairCount => _pairs.Count;

        public IReadOnlyList<KerningPair> Pairs => _pairs;

        public void ClearPairs()
        {
            _pairs.Clear();
            _owner.MarkDirty();
        }

        public void AddOrReplacePair(ushort left, ushort right, short value)
        {
            for (int i = _pairs.Count - 1; i >= 0; i--)
            {
                var p = _pairs[i];
                if (p.Left == left && p.Right == right)
                    _pairs.RemoveAt(i);
            }

            _pairs.Add(new KerningPair(left, right, value));
            _owner.MarkDirty();
        }

        internal void AddPairUnchecked(ushort left, ushort right, short value)
            => _pairs.Add(new KerningPair(left, right, value));

        public bool RemovePair(ushort left, ushort right)
        {
            bool removed = false;
            for (int i = _pairs.Count - 1; i >= 0; i--)
            {
                var p = _pairs[i];
                if (p.Left == left && p.Right == right)
                {
                    _pairs.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
                _owner.MarkDirty();

            return removed;
        }

        internal void SortPairs()
        {
            _pairs.Sort(static (a, b) => a.CompareTo(b));
        }

        public readonly struct KerningPair : IComparable<KerningPair>
        {
            public ushort Left { get; }
            public ushort Right { get; }
            public short Value { get; }

            public KerningPair(ushort left, ushort right, short value)
            {
                Left = left;
                Right = right;
                Value = value;
            }

            public int CompareTo(KerningPair other)
            {
                int c = Left.CompareTo(other.Left);
                if (c != 0) return c;
                return Right.CompareTo(other.Right);
            }
        }
    }
}
