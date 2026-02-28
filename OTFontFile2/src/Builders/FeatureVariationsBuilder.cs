namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType Layout <c>FeatureVariations</c> table referenced by GSUB/GPOS version 1.1.
/// </summary>
public sealed class FeatureVariationsBuilder
{
    private readonly OtlLayoutTableBuilder _owner;
    private readonly List<FeatureVariationRecordBuilder> _records = new();

    internal FeatureVariationsBuilder(OtlLayoutTableBuilder owner)
        => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public int RecordCount => _records.Count;

    public void Clear()
    {
        if (_records.Count == 0)
            return;

        _records.Clear();
        _owner.MarkDirty();
    }

    public FeatureVariationRecordBuilder AddRecord()
    {
        EnsureLayoutVersion11();
        var r = new FeatureVariationRecordBuilder(_owner);
        _records.Add(r);
        _owner.MarkDirty();
        return r;
    }

    internal byte[] BuildBytes(
        Dictionary<OtlLayoutTableBuilder.FeatureBuilder, ushort> featureIndexByFeature,
        Dictionary<OtlLayoutTableBuilder.LookupBuilder, ushort> lookupIndexByLookup)
    {
        if (_records.Count == 0)
            return Array.Empty<byte>();

        // Spec: FeatureVariations.version = 1.0
        const ushort majorVersion = 1;
        const ushort minorVersion = 0;

        int count = _records.Count;
        uint countU32 = (uint)count;

        var condTables = new byte[count][];
        var substTables = new byte[count][];

        for (int i = 0; i < count; i++)
        {
            var r = _records[i];
            condTables[i] = r.Conditions.BuildBytes();
            substTables[i] = r.Substitution.BuildBytes(featureIndexByFeature, lookupIndexByLookup);
        }

        int headerLen = checked(8 + (count * 8)); // version(4) + count(4) + records(8 each)
        int pos = headerLen;

        Span<int> condOffsets = count <= 64 ? stackalloc int[count] : new int[count];
        Span<int> substOffsets = count <= 64 ? stackalloc int[count] : new int[count];

        for (int i = 0; i < count; i++)
        {
            pos = Align2(pos);
            condOffsets[i] = pos;
            pos = checked(pos + condTables[i].Length);

            pos = Align2(pos);
            substOffsets[i] = pos;
            pos = checked(pos + substTables[i].Length);
        }

        var table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, majorVersion);
        BigEndian.WriteUInt16(span, 2, minorVersion);
        BigEndian.WriteUInt32(span, 4, countU32);

        int recordPos = 8;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt32(span, recordPos + 0, checked((uint)condOffsets[i]));
            BigEndian.WriteUInt32(span, recordPos + 4, checked((uint)substOffsets[i]));
            recordPos += 8;
        }

        for (int i = 0; i < count; i++)
        {
            condTables[i].CopyTo(span.Slice(condOffsets[i]));
            substTables[i].CopyTo(span.Slice(substOffsets[i]));
        }

        return table;
    }

    private void EnsureLayoutVersion11()
    {
        if (_owner.Version.RawValue < 0x00010001u)
            _owner.Version = new Fixed1616(0x00010001u);
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    public sealed class FeatureVariationRecordBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;

        internal FeatureVariationRecordBuilder(OtlLayoutTableBuilder owner)
        {
            _owner = owner;
            Conditions = new ConditionSetBuilder(owner);
            Substitution = new FeatureTableSubstitutionBuilder(owner);
        }

        public ConditionSetBuilder Conditions { get; }
        public FeatureTableSubstitutionBuilder Substitution { get; }
    }

    public sealed class ConditionSetBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<ConditionFormat1> _conditions = new();

        internal ConditionSetBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int ConditionCount => _conditions.Count;

        public void Clear()
        {
            if (_conditions.Count == 0)
                return;

            _conditions.Clear();
            _owner.MarkDirty();
        }

        public void AddConditionFormat1(ushort axisIndex, F2Dot14 filterRangeMinValue, F2Dot14 filterRangeMaxValue)
        {
            _conditions.Add(new ConditionFormat1(axisIndex, filterRangeMinValue, filterRangeMaxValue));
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes()
        {
            if (_conditions.Count > ushort.MaxValue)
                throw new InvalidOperationException("ConditionCount must fit in uint16.");

            int count = _conditions.Count;
            int headerLen = checked(2 + (count * 4));
            int totalLen = checked(headerLen + (count * 8));

            var bytes = new byte[totalLen];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)count));

            int offsetsPos = 2;
            int condPos = headerLen;
            for (int i = 0; i < count; i++)
            {
                // Offset32 from start of ConditionSet.
                BigEndian.WriteUInt32(span, offsetsPos, checked((uint)condPos));
                offsetsPos += 4;

                // ConditionFormat1:
                // conditionFormat(2)=1, axisIndex(2), min(2), max(2)
                var c = _conditions[i];
                BigEndian.WriteUInt16(span, condPos + 0, 1);
                BigEndian.WriteUInt16(span, condPos + 2, c.AxisIndex);
                BigEndian.WriteInt16(span, condPos + 4, c.FilterRangeMinValue.RawValue);
                BigEndian.WriteInt16(span, condPos + 6, c.FilterRangeMaxValue.RawValue);
                condPos += 8;
            }

            return bytes;
        }

        private readonly struct ConditionFormat1
        {
            public ushort AxisIndex { get; }
            public F2Dot14 FilterRangeMinValue { get; }
            public F2Dot14 FilterRangeMaxValue { get; }

            public ConditionFormat1(ushort axisIndex, F2Dot14 filterRangeMinValue, F2Dot14 filterRangeMaxValue)
            {
                AxisIndex = axisIndex;
                FilterRangeMinValue = filterRangeMinValue;
                FilterRangeMaxValue = filterRangeMaxValue;
            }
        }
    }

    public sealed class FeatureTableSubstitutionBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<Entry> _entries = new();

        internal FeatureTableSubstitutionBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int SubstitutionCount => _entries.Count;

        public void Clear()
        {
            if (_entries.Count == 0)
                return;

            _entries.Clear();
            _owner.MarkDirty();
        }

        public FeatureTableBuilder CreateReplacementFeatureTable() => new(_owner);

        public void AddOrReplaceSubstitution(OtlLayoutTableBuilder.FeatureBuilder feature, FeatureTableBuilder replacementFeatureTable)
        {
            if (feature is null) throw new ArgumentNullException(nameof(feature));
            if (replacementFeatureTable is null) throw new ArgumentNullException(nameof(replacementFeatureTable));

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_entries[i].Feature, feature))
                    _entries.RemoveAt(i);
            }

            _entries.Add(new Entry(feature, replacementFeatureTable));
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(
            Dictionary<OtlLayoutTableBuilder.FeatureBuilder, ushort> featureIndexByFeature,
            Dictionary<OtlLayoutTableBuilder.LookupBuilder, ushort> lookupIndexByLookup)
        {
            const ushort version = 1;

            if (_entries.Count == 0)
            {
                byte[] empty = new byte[4];
                var emptySpan = empty.AsSpan();
                BigEndian.WriteUInt16(emptySpan, 0, version);
                BigEndian.WriteUInt16(emptySpan, 2, 0);
                return empty;
            }

            var entries = new SubstEntry[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!featureIndexByFeature.TryGetValue(e.Feature, out ushort idx))
                    throw new InvalidOperationException("FeatureTableSubstitution references a feature that is not present in the FeatureList.");

                entries[i] = new SubstEntry(idx, e.ReplacementFeatureTable);
            }

            Array.Sort(entries, static (a, b) => a.FeatureIndex.CompareTo(b.FeatureIndex));

            // Deduplicate (keep last).
            int uniqueCount = 1;
            for (int i = 1; i < entries.Length; i++)
            {
                if (entries[i].FeatureIndex == entries[uniqueCount - 1].FeatureIndex)
                {
                    entries[uniqueCount - 1] = entries[i];
                    continue;
                }

                entries[uniqueCount++] = entries[i];
            }

            if (uniqueCount > ushort.MaxValue)
                throw new InvalidOperationException("SubstitutionCount must fit in uint16.");

            var featureTables = new byte[uniqueCount][];
            for (int i = 0; i < uniqueCount; i++)
                featureTables[i] = entries[i].ReplacementFeatureTable.BuildBytes(lookupIndexByLookup);

            int headerLen = checked(4 + (uniqueCount * 6)); // version(2) + count(2) + records(6 each)
            int pos = headerLen;

            Span<int> featureOffsets = uniqueCount <= 64 ? stackalloc int[uniqueCount] : new int[uniqueCount];
            for (int i = 0; i < uniqueCount; i++)
            {
                pos = Align2(pos);
                featureOffsets[i] = pos;
                pos = checked(pos + featureTables[i].Length);
            }

            var bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, version);
            BigEndian.WriteUInt16(span, 2, checked((ushort)uniqueCount));

            int recPos = 4;
            for (int i = 0; i < uniqueCount; i++)
            {
                BigEndian.WriteUInt16(span, recPos + 0, entries[i].FeatureIndex);
                BigEndian.WriteUInt32(span, recPos + 2, checked((uint)featureOffsets[i]));
                recPos += 6;
            }

            for (int i = 0; i < uniqueCount; i++)
                featureTables[i].CopyTo(span.Slice(featureOffsets[i]));

            return bytes;
        }

        private readonly struct Entry
        {
            public OtlLayoutTableBuilder.FeatureBuilder Feature { get; }
            public FeatureTableBuilder ReplacementFeatureTable { get; }

            public Entry(OtlLayoutTableBuilder.FeatureBuilder feature, FeatureTableBuilder replacementFeatureTable)
            {
                Feature = feature;
                ReplacementFeatureTable = replacementFeatureTable;
            }
        }

        private readonly struct SubstEntry
        {
            public ushort FeatureIndex { get; }
            public FeatureTableBuilder ReplacementFeatureTable { get; }

            public SubstEntry(ushort featureIndex, FeatureTableBuilder replacementFeatureTable)
            {
                FeatureIndex = featureIndex;
                ReplacementFeatureTable = replacementFeatureTable;
            }
        }
    }

    public sealed class FeatureTableBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<OtlLayoutTableBuilder.LookupBuilder> _lookups = new();
        private ReadOnlyMemory<byte> _featureParams;

        internal FeatureTableBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int LookupCount => _lookups.Count;

        public bool HasFeatureParams => !_featureParams.IsEmpty;

        public void ClearFeatureParams()
        {
            if (_featureParams.IsEmpty)
                return;

            _featureParams = ReadOnlyMemory<byte>.Empty;
            _owner.MarkDirty();
        }

        public void SetFeatureParams(ReadOnlyMemory<byte> featureParams)
        {
            if (featureParams.IsEmpty)
                throw new ArgumentException("Feature params must be non-empty. Use ClearFeatureParams() instead.", nameof(featureParams));

            _featureParams = featureParams;
            _owner.MarkDirty();
        }

        public void ClearLookups()
        {
            if (_lookups.Count == 0)
                return;

            _lookups.Clear();
            _owner.MarkDirty();
        }

        public void AddLookup(OtlLayoutTableBuilder.LookupBuilder lookup)
        {
            if (lookup is null) throw new ArgumentNullException(nameof(lookup));
            _lookups.Add(lookup);
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(Dictionary<OtlLayoutTableBuilder.LookupBuilder, ushort> lookupIndexByLookup)
        {
            Span<ushort> lookupIndices = _lookups.Count <= 64 ? stackalloc ushort[_lookups.Count] : new ushort[_lookups.Count];
            int count = 0;

            var seen = new HashSet<OtlLayoutTableBuilder.LookupBuilder>();
            for (int i = 0; i < _lookups.Count; i++)
            {
                var l = _lookups[i];
                if (l is null)
                    continue;
                if (!seen.Add(l))
                    continue;

                if (!lookupIndexByLookup.TryGetValue(l, out ushort idx))
                    throw new InvalidOperationException("FeatureTable references a lookup that is not present in the LookupList.");

                lookupIndices[count++] = idx;
            }

            if (count > ushort.MaxValue)
                throw new InvalidOperationException("LookupIndexCount must fit in uint16.");

            int headerLen = checked(4 + (count * 2));
            int paramsOffset = 0;
            int totalLen = headerLen;

            if (!_featureParams.IsEmpty)
            {
                paramsOffset = headerLen;
                totalLen = checked(headerLen + _featureParams.Length);

                if (paramsOffset > ushort.MaxValue)
                    throw new InvalidOperationException("FeatureParamsOffset must fit in uint16.");
            }

            byte[] bytes = new byte[totalLen];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)paramsOffset));
            BigEndian.WriteUInt16(span, 2, checked((ushort)count));

            int o = 4;
            for (int i = 0; i < count; i++)
            {
                BigEndian.WriteUInt16(span, o, lookupIndices[i]);
                o += 2;
            }

            if (paramsOffset != 0)
                _featureParams.Span.CopyTo(span.Slice(paramsOffset, _featureParams.Length));

            return bytes;
        }
    }
}
