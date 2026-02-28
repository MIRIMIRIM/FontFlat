namespace OTFontFile2.Tables;

public enum OtlLayoutKind
{
    Gsub,
    Gpos
}

/// <summary>
/// Mutable builder for the OpenType Layout common table format used by GSUB and GPOS.
/// </summary>
public sealed class OtlLayoutTableBuilder
{
    private readonly Action _markDirty;
    private readonly OtlLayoutKind _kind;

    private Fixed1616 _version = new(0x00010000u);

    public OtlLayoutTableBuilder(Action markDirty, OtlLayoutKind kind = OtlLayoutKind.Gsub)
    {
        _markDirty = markDirty ?? throw new ArgumentNullException(nameof(markDirty));
        _kind = kind;

        Scripts = new ScriptListBuilder(this);
        Features = new FeatureListBuilder(this);
        Lookups = new LookupListBuilder(this);
        FeatureVariations = new FeatureVariationsBuilder(this);
    }

    internal void MarkDirty() => _markDirty();

    public Fixed1616 Version
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

    public ScriptListBuilder Scripts { get; }
    public FeatureListBuilder Features { get; }
    public LookupListBuilder Lookups { get; }
    public FeatureVariationsBuilder FeatureVariations { get; }

    public void Clear()
    {
        Version = new Fixed1616(0x00010000u);
        Scripts.Clear();
        Features.Clear();
        Lookups.Clear();
        FeatureVariations.Clear();
        MarkDirty();
    }

    internal byte[] BuildBytes()
    {
        uint versionRaw = Version.RawValue;
        int headerLen = versionRaw >= 0x00010001u ? 14 : 10;

        var lookupIndexByLookup = new Dictionary<LookupBuilder, ushort>(Lookups.LookupCount);
        for (int i = 0; i < Lookups.LookupCount; i++)
            lookupIndexByLookup.Add(Lookups.Lookups[i], checked((ushort)i));

        var orderedFeatures = Features.GetOrderedFeatures();
        var featureIndexByFeature = new Dictionary<FeatureBuilder, ushort>(orderedFeatures.Length);
        for (int i = 0; i < orderedFeatures.Length; i++)
            featureIndexByFeature.Add(orderedFeatures[i].feature, checked((ushort)i));

        byte[] featureVariationsBytes = FeatureVariations.BuildBytes(featureIndexByFeature, lookupIndexByLookup);
        if (featureVariationsBytes.Length != 0 && headerLen != 14)
            throw new InvalidOperationException("FeatureVariations require layout Version >= 1.1.");

        byte[] lookupListBytes = Lookups.BuildBytes(_kind);
        byte[] featureListBytes = Features.BuildBytes(orderedFeatures, lookupIndexByLookup);
        byte[] scriptListBytes = Scripts.BuildBytes(featureIndexByFeature);

        int pos = headerLen;

        int scriptListOffset = 0;
        if (scriptListBytes.Length != 0)
        {
            pos = Align2(pos);
            scriptListOffset = pos;
            pos = checked(pos + scriptListBytes.Length);
        }

        int featureListOffset = 0;
        if (featureListBytes.Length != 0)
        {
            pos = Align2(pos);
            featureListOffset = pos;
            pos = checked(pos + featureListBytes.Length);
        }

        int lookupListOffset = 0;
        if (lookupListBytes.Length != 0)
        {
            pos = Align2(pos);
            lookupListOffset = pos;
            pos = checked(pos + lookupListBytes.Length);
        }

        int featureVariationsOffset = 0;
        if (headerLen == 14 && featureVariationsBytes.Length != 0)
        {
            pos = Align2(pos);
            featureVariationsOffset = pos;
            pos = checked(pos + featureVariationsBytes.Length);
        }

        if (scriptListOffset > ushort.MaxValue) throw new InvalidOperationException("ScriptListOffset must fit in uint16.");
        if (featureListOffset > ushort.MaxValue) throw new InvalidOperationException("FeatureListOffset must fit in uint16.");
        if (lookupListOffset > ushort.MaxValue) throw new InvalidOperationException("LookupListOffset must fit in uint16.");

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, versionRaw);
        BigEndian.WriteUInt16(span, 4, (ushort)scriptListOffset);
        BigEndian.WriteUInt16(span, 6, (ushort)featureListOffset);
        BigEndian.WriteUInt16(span, 8, (ushort)lookupListOffset);

        if (headerLen == 14)
        {
            BigEndian.WriteUInt32(span, 10, checked((uint)featureVariationsOffset));
        }

        if (scriptListOffset != 0)
            scriptListBytes.CopyTo(span.Slice(scriptListOffset));
        if (featureListOffset != 0)
            featureListBytes.CopyTo(span.Slice(featureListOffset));
        if (lookupListOffset != 0)
            lookupListBytes.CopyTo(span.Slice(lookupListOffset));
        if (featureVariationsOffset != 0)
            featureVariationsBytes.CopyTo(span.Slice(featureVariationsOffset));

        return table;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    public sealed class ScriptListBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<ScriptEntry> _scripts = new();

        internal ScriptListBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int ScriptCount => _scripts.Count;
        public IReadOnlyList<ScriptEntry> Scripts => _scripts;

        public void Clear()
        {
            if (_scripts.Count == 0)
                return;

            _scripts.Clear();
            _owner.MarkDirty();
        }

        public ScriptBuilder GetOrAddScript(Tag scriptTag)
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                var e = _scripts[i];
                if (e.ScriptTag == scriptTag)
                    return e.Script;
            }

            var s = new ScriptBuilder(_owner);
            _scripts.Add(new ScriptEntry(scriptTag, s));
            _owner.MarkDirty();
            return s;
        }

        public bool RemoveScript(Tag scriptTag)
        {
            bool removed = false;
            for (int i = _scripts.Count - 1; i >= 0; i--)
            {
                if (_scripts[i].ScriptTag == scriptTag)
                {
                    _scripts.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
                _owner.MarkDirty();

            return removed;
        }

        internal byte[] BuildBytes(Dictionary<FeatureBuilder, ushort> featureIndexByFeature)
        {
            if (_scripts.Count == 0)
                return Array.Empty<byte>();

            var scripts = _scripts.ToArray();
            Array.Sort(scripts, static (a, b) => a.ScriptTag.CompareTo(b.ScriptTag));

            int uniqueCount = 1;
            for (int i = 1; i < scripts.Length; i++)
            {
                if (scripts[i].ScriptTag == scripts[uniqueCount - 1].ScriptTag)
                {
                    scripts[uniqueCount - 1] = scripts[i];
                    continue;
                }

                scripts[uniqueCount++] = scripts[i];
            }

            if (uniqueCount > ushort.MaxValue)
                throw new InvalidOperationException("ScriptCount must fit in uint16.");

            int headerLen = checked(2 + (uniqueCount * 6));
            int pos = headerLen;

            Span<int> scriptOffsets = uniqueCount <= 64 ? stackalloc int[uniqueCount] : new int[uniqueCount];
            var scriptTables = new byte[uniqueCount][];

            for (int i = 0; i < uniqueCount; i++)
            {
                byte[] scriptBytes = scripts[i].Script.BuildBytes(featureIndexByFeature);
                pos = Align2(pos);
                scriptOffsets[i] = pos;
                scriptTables[i] = scriptBytes;
                pos = checked(pos + scriptBytes.Length);
            }

            for (int i = 0; i < uniqueCount; i++)
            {
                if (scriptOffsets[i] > ushort.MaxValue)
                    throw new InvalidOperationException("ScriptOffset must fit in uint16.");
            }

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)uniqueCount));
            int o = 2;
            for (int i = 0; i < uniqueCount; i++)
            {
                BigEndian.WriteUInt32(span, o + 0, scripts[i].ScriptTag.Value);
                BigEndian.WriteUInt16(span, o + 4, checked((ushort)scriptOffsets[i]));
                o += 6;
            }

            for (int i = 0; i < uniqueCount; i++)
                scriptTables[i].CopyTo(span.Slice(scriptOffsets[i]));

            return bytes;
        }

        public readonly struct ScriptEntry
        {
            public Tag ScriptTag { get; }
            public ScriptBuilder Script { get; }

            internal ScriptEntry(Tag scriptTag, ScriptBuilder script)
            {
                ScriptTag = scriptTag;
                Script = script;
            }
        }
    }

    public sealed class ScriptBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private LangSysBuilder? _defaultLangSys;
        private readonly List<LangSysEntry> _langSys = new();

        internal ScriptBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public bool HasDefaultLangSys => _defaultLangSys is not null;

        public LangSysBuilder GetOrCreateDefaultLangSys()
        {
            if (_defaultLangSys is not null)
                return _defaultLangSys;

            _defaultLangSys = new LangSysBuilder(_owner);
            _owner.MarkDirty();
            return _defaultLangSys;
        }

        public void ClearDefaultLangSys()
        {
            if (_defaultLangSys is null)
                return;

            _defaultLangSys = null;
            _owner.MarkDirty();
        }

        public LangSysBuilder GetOrAddLangSys(Tag langSysTag)
        {
            for (int i = 0; i < _langSys.Count; i++)
            {
                var e = _langSys[i];
                if (e.LangSysTag == langSysTag)
                    return e.LangSys;
            }

            var ls = new LangSysBuilder(_owner);
            _langSys.Add(new LangSysEntry(langSysTag, ls));
            _owner.MarkDirty();
            return ls;
        }

        public bool RemoveLangSys(Tag langSysTag)
        {
            bool removed = false;
            for (int i = _langSys.Count - 1; i >= 0; i--)
            {
                if (_langSys[i].LangSysTag == langSysTag)
                {
                    _langSys.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
                _owner.MarkDirty();

            return removed;
        }

        public void ClearLangSys()
        {
            if (_langSys.Count == 0)
                return;

            _langSys.Clear();
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(Dictionary<FeatureBuilder, ushort> featureIndexByFeature)
        {
            var langSys = _langSys.Count == 0 ? Array.Empty<LangSysEntry>() : _langSys.ToArray();
            if (langSys.Length != 0)
                Array.Sort(langSys, static (a, b) => a.LangSysTag.CompareTo(b.LangSysTag));

            int uniqueCount = 0;
            for (int i = 0; i < langSys.Length; i++)
            {
                if (uniqueCount != 0 && langSys[i].LangSysTag == langSys[uniqueCount - 1].LangSysTag)
                {
                    langSys[uniqueCount - 1] = langSys[i];
                    continue;
                }

                langSys[uniqueCount++] = langSys[i];
            }

            if (uniqueCount > ushort.MaxValue)
                throw new InvalidOperationException("LangSysCount must fit in uint16.");

            int headerLen = checked(4 + (uniqueCount * 6));
            int pos = headerLen;

            byte[]? defaultLangBytes = null;
            int defaultLangOffset = 0;
            if (_defaultLangSys is not null)
            {
                defaultLangBytes = _defaultLangSys.BuildBytes(featureIndexByFeature);
                pos = Align2(pos);
                defaultLangOffset = pos;
                pos = checked(pos + defaultLangBytes.Length);
                if (defaultLangOffset > ushort.MaxValue)
                    throw new InvalidOperationException("DefaultLangSysOffset must fit in uint16.");
            }

            Span<int> langSysOffsets = uniqueCount <= 64 ? stackalloc int[uniqueCount] : new int[uniqueCount];
            var langSysTables = new byte[uniqueCount][];

            for (int i = 0; i < uniqueCount; i++)
            {
                byte[] lsBytes = langSys[i].LangSys.BuildBytes(featureIndexByFeature);
                pos = Align2(pos);
                langSysOffsets[i] = pos;
                langSysTables[i] = lsBytes;
                pos = checked(pos + lsBytes.Length);

                if (langSysOffsets[i] > ushort.MaxValue)
                    throw new InvalidOperationException("LangSysOffset must fit in uint16.");
            }

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)defaultLangOffset));
            BigEndian.WriteUInt16(span, 2, checked((ushort)uniqueCount));

            int o = 4;
            for (int i = 0; i < uniqueCount; i++)
            {
                BigEndian.WriteUInt32(span, o + 0, langSys[i].LangSysTag.Value);
                BigEndian.WriteUInt16(span, o + 4, checked((ushort)langSysOffsets[i]));
                o += 6;
            }

            if (defaultLangBytes is not null)
                defaultLangBytes.CopyTo(span.Slice(defaultLangOffset));

            for (int i = 0; i < uniqueCount; i++)
                langSysTables[i].CopyTo(span.Slice(langSysOffsets[i]));

            return bytes;
        }

        private readonly struct LangSysEntry
        {
            public Tag LangSysTag { get; }
            public LangSysBuilder LangSys { get; }

            public LangSysEntry(Tag langSysTag, LangSysBuilder langSys)
            {
                LangSysTag = langSysTag;
                LangSys = langSys;
            }
        }
    }

    public sealed class LangSysBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private FeatureBuilder? _requiredFeature;
        private readonly List<FeatureBuilder> _features = new();

        internal LangSysBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public FeatureBuilder? RequiredFeature
        {
            get => _requiredFeature;
            set
            {
                if (ReferenceEquals(value, _requiredFeature))
                    return;

                _requiredFeature = value;
                _owner.MarkDirty();
            }
        }

        public IReadOnlyList<FeatureBuilder> Features => _features;

        public void ClearFeatures()
        {
            if (_features.Count == 0)
                return;

            _features.Clear();
            _owner.MarkDirty();
        }

        public void AddFeature(FeatureBuilder feature)
        {
            if (feature is null) throw new ArgumentNullException(nameof(feature));
            _features.Add(feature);
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(Dictionary<FeatureBuilder, ushort> featureIndexByFeature)
        {
            ushort requiredIndex = 0xFFFF;
            if (_requiredFeature is not null)
            {
                if (!featureIndexByFeature.TryGetValue(_requiredFeature, out requiredIndex))
                    throw new InvalidOperationException("RequiredFeature is not present in the FeatureList.");
            }

            Span<ushort> featureIndices = _features.Count <= 64 ? stackalloc ushort[_features.Count] : new ushort[_features.Count];
            int count = 0;

            var seen = new HashSet<FeatureBuilder>();
            for (int i = 0; i < _features.Count; i++)
            {
                var f = _features[i];
                if (f is null)
                    continue;
                if (!seen.Add(f))
                    continue;

                if (!featureIndexByFeature.TryGetValue(f, out ushort idx))
                    throw new InvalidOperationException("LangSys references a feature that is not present in the FeatureList.");

                featureIndices[count++] = idx;
            }

            if (count > ushort.MaxValue)
                throw new InvalidOperationException("FeatureIndexCount must fit in uint16.");

            byte[] bytes = new byte[checked(6 + (count * 2))];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, 0); // LookupOrder (reserved)
            BigEndian.WriteUInt16(span, 2, requiredIndex);
            BigEndian.WriteUInt16(span, 4, checked((ushort)count));

            int o = 6;
            for (int i = 0; i < count; i++)
            {
                BigEndian.WriteUInt16(span, o, featureIndices[i]);
                o += 2;
            }

            return bytes;
        }
    }

    public sealed class FeatureListBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<FeatureEntry> _features = new();

        internal FeatureListBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int FeatureCount => _features.Count;
        public IReadOnlyList<FeatureEntry> Features => _features;

        public void Clear()
        {
            if (_features.Count == 0)
                return;

            _features.Clear();
            _owner.MarkDirty();
        }

        public FeatureBuilder GetOrAddFeature(Tag featureTag)
        {
            for (int i = 0; i < _features.Count; i++)
            {
                var e = _features[i];
                if (e.FeatureTag == featureTag)
                    return e.Feature;
            }

            var f = new FeatureBuilder(_owner, featureTag);
            _features.Add(new FeatureEntry(featureTag, f));
            _owner.MarkDirty();
            return f;
        }

        public bool RemoveFeature(Tag featureTag)
        {
            bool removed = false;
            for (int i = _features.Count - 1; i >= 0; i--)
            {
                if (_features[i].FeatureTag == featureTag)
                {
                    _features.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
                _owner.MarkDirty();

            return removed;
        }

        internal (Tag tag, FeatureBuilder feature)[] GetOrderedFeatures()
        {
            if (_features.Count == 0)
                return Array.Empty<(Tag tag, FeatureBuilder feature)>();

            var features = new (Tag tag, FeatureBuilder feature)[_features.Count];
            for (int i = 0; i < _features.Count; i++)
                features[i] = (_features[i].FeatureTag, _features[i].Feature);

            Array.Sort(features, static (a, b) => a.tag.CompareTo(b.tag));

            // Deduplicate (keep last).
            int uniqueCount = 1;
            for (int i = 1; i < features.Length; i++)
            {
                if (features[i].tag == features[uniqueCount - 1].tag)
                {
                    features[uniqueCount - 1] = features[i];
                    continue;
                }

                features[uniqueCount++] = features[i];
            }

            if (uniqueCount == features.Length)
                return features;

            var resized = new (Tag tag, FeatureBuilder feature)[uniqueCount];
            Array.Copy(features, resized, uniqueCount);
            return resized;
        }

        internal byte[] BuildBytes((Tag tag, FeatureBuilder feature)[] orderedFeatures, Dictionary<LookupBuilder, ushort> lookupIndexByLookup)
        {
            if (orderedFeatures.Length == 0)
                return Array.Empty<byte>();

            if (orderedFeatures.Length > ushort.MaxValue)
                throw new InvalidOperationException("FeatureCount must fit in uint16.");

            int headerLen = checked(2 + (orderedFeatures.Length * 6));
            int pos = headerLen;

            Span<int> featureOffsets = orderedFeatures.Length <= 128 ? stackalloc int[orderedFeatures.Length] : new int[orderedFeatures.Length];
            var featureTables = new byte[orderedFeatures.Length][];

            for (int i = 0; i < orderedFeatures.Length; i++)
            {
                byte[] featureBytes = orderedFeatures[i].feature.BuildBytes(lookupIndexByLookup);
                pos = Align2(pos);
                featureOffsets[i] = pos;
                featureTables[i] = featureBytes;
                pos = checked(pos + featureBytes.Length);

                if (featureOffsets[i] > ushort.MaxValue)
                    throw new InvalidOperationException("FeatureOffset must fit in uint16.");
            }

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)orderedFeatures.Length));

            int o = 2;
            for (int i = 0; i < orderedFeatures.Length; i++)
            {
                BigEndian.WriteUInt32(span, o + 0, orderedFeatures[i].tag.Value);
                BigEndian.WriteUInt16(span, o + 4, checked((ushort)featureOffsets[i]));
                o += 6;
            }

            for (int i = 0; i < orderedFeatures.Length; i++)
                featureTables[i].CopyTo(span.Slice(featureOffsets[i]));

            return bytes;
        }

        public readonly struct FeatureEntry
        {
            public Tag FeatureTag { get; }
            public FeatureBuilder Feature { get; }

            internal FeatureEntry(Tag featureTag, FeatureBuilder feature)
            {
                FeatureTag = featureTag;
                Feature = feature;
            }
        }
    }

    public sealed class FeatureBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<LookupBuilder> _lookups = new();
        private ReadOnlyMemory<byte> _featureParams;

        internal FeatureBuilder(OtlLayoutTableBuilder owner, Tag featureTag)
        {
            _owner = owner;
            FeatureTag = featureTag;
        }

        public Tag FeatureTag { get; }

        public int LookupCount => _lookups.Count;
        public IReadOnlyList<LookupBuilder> Lookups => _lookups;

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

        public void AddLookup(LookupBuilder lookup)
        {
            if (lookup is null) throw new ArgumentNullException(nameof(lookup));
            _lookups.Add(lookup);
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(Dictionary<LookupBuilder, ushort> lookupIndexByLookup)
        {
            Span<ushort> lookupIndices = _lookups.Count <= 64 ? stackalloc ushort[_lookups.Count] : new ushort[_lookups.Count];
            int count = 0;

            var seen = new HashSet<LookupBuilder>();
            for (int i = 0; i < _lookups.Count; i++)
            {
                var l = _lookups[i];
                if (l is null)
                    continue;
                if (!seen.Add(l))
                    continue;

                if (!lookupIndexByLookup.TryGetValue(l, out ushort idx))
                    throw new InvalidOperationException("Feature references a lookup that is not present in the LookupList.");

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

    public sealed class LookupListBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<LookupBuilder> _lookups = new();

        internal LookupListBuilder(OtlLayoutTableBuilder owner) => _owner = owner;

        public int LookupCount => _lookups.Count;
        public IReadOnlyList<LookupBuilder> Lookups => _lookups;

        public void Clear()
        {
            if (_lookups.Count == 0)
                return;

            _lookups.Clear();
            _owner.MarkDirty();
        }

        public LookupBuilder AddLookup(ushort lookupType, ushort lookupFlag = 0)
        {
            var l = new LookupBuilder(_owner, lookupType, lookupFlag);
            _lookups.Add(l);
            _owner.MarkDirty();
            return l;
        }

        public bool RemoveLookup(LookupBuilder lookup)
        {
            if (lookup is null) throw new ArgumentNullException(nameof(lookup));

            bool removed = _lookups.Remove(lookup);
            if (removed)
                _owner.MarkDirty();

            return removed;
        }

        internal byte[] BuildBytes(OtlLayoutKind kind)
        {
            if (_lookups.Count == 0)
                return Array.Empty<byte>();

            if (_lookups.Count > ushort.MaxValue)
                throw new InvalidOperationException("LookupCount must fit in uint16.");

            int count = _lookups.Count;

            int headerLen = checked(2 + (count * 2));
            int pos = headerLen;

            Span<int> lookupOffsets = count <= 128 ? stackalloc int[count] : new int[count];
            var lookupTables = new byte[count][];

            for (int i = 0; i < count; i++)
            {
                byte[] lookupBytes = _lookups[i].BuildBytes(kind);
                pos = Align2(pos);
                lookupOffsets[i] = pos;
                lookupTables[i] = lookupBytes;
                pos = checked(pos + lookupBytes.Length);

                if (lookupOffsets[i] > ushort.MaxValue)
                    throw new InvalidOperationException("LookupOffset must fit in uint16.");
            }

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, checked((ushort)count));
            int o = 2;
            for (int i = 0; i < count; i++)
            {
                BigEndian.WriteUInt16(span, o, checked((ushort)lookupOffsets[i]));
                o += 2;
            }

            for (int i = 0; i < count; i++)
                lookupTables[i].CopyTo(span.Slice(lookupOffsets[i]));

            return bytes;
        }
    }

    public sealed class LookupBuilder
    {
        private readonly OtlLayoutTableBuilder _owner;
        private readonly List<ReadOnlyMemory<byte>> _subtables = new();
        private ushort _lookupFlag;
        private ushort _markFilteringSet;

        internal LookupBuilder(OtlLayoutTableBuilder owner, ushort lookupType, ushort lookupFlag)
        {
            _owner = owner;
            LookupType = lookupType;
            _lookupFlag = lookupFlag;
        }

        public ushort LookupType { get; }

        public ushort LookupFlag
        {
            get => _lookupFlag;
            set
            {
                if (value == _lookupFlag)
                    return;

                _lookupFlag = value;
                _owner.MarkDirty();
            }
        }

        public bool UsesMarkFilteringSet => (LookupFlag & OtlLayoutTable.Lookup.UseMarkFilteringSetFlag) != 0;

        public ushort MarkFilteringSet
        {
            get => _markFilteringSet;
            set
            {
                if (value == _markFilteringSet)
                    return;

                _markFilteringSet = value;
                _owner.MarkDirty();
            }
        }

        public int SubtableCount => _subtables.Count;

        public void ClearSubtables()
        {
            if (_subtables.Count == 0)
                return;

            _subtables.Clear();
            _owner.MarkDirty();
        }

        public void AddSubtable(ReadOnlyMemory<byte> subtableBytes)
        {
            _subtables.Add(subtableBytes);
            _owner.MarkDirty();
        }

        internal byte[] BuildBytes(OtlLayoutKind kind)
        {
            if (_subtables.Count > ushort.MaxValue)
                throw new InvalidOperationException("SubtableCount must fit in uint16.");

            int subCount = _subtables.Count;

            int headerLen = checked(6 + (subCount * 2) + (UsesMarkFilteringSet ? 2 : 0));
            int pos = headerLen;

            Span<int> subOffsets = subCount <= 128 ? stackalloc int[subCount] : new int[subCount];
            var subTables = new ReadOnlyMemory<byte>[subCount];

            bool overflow = false;
            for (int i = 0; i < subCount; i++)
            {
                var sub = _subtables[i];
                pos = Align2(pos);
                subOffsets[i] = pos;
                subTables[i] = sub;
                pos = checked(pos + sub.Length);

                if (subOffsets[i] > ushort.MaxValue)
                {
                    overflow = true;
                    break;
                }
            }

            if (overflow)
                return BuildBytesWithExtensionHeaders(kind);

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            BigEndian.WriteUInt16(span, 0, LookupType);
            BigEndian.WriteUInt16(span, 2, LookupFlag);
            BigEndian.WriteUInt16(span, 4, checked((ushort)subCount));

            int o = 6;
            for (int i = 0; i < subCount; i++)
            {
                BigEndian.WriteUInt16(span, o, checked((ushort)subOffsets[i]));
                o += 2;
            }

            if (UsesMarkFilteringSet)
                BigEndian.WriteUInt16(span, o, MarkFilteringSet);

            for (int i = 0; i < subCount; i++)
                subTables[i].Span.CopyTo(span.Slice(subOffsets[i]));

            return bytes;
        }

        private byte[] BuildBytesWithExtensionHeaders(OtlLayoutKind kind)
        {
            ushort extensionLookupType = kind == OtlLayoutKind.Gsub ? (ushort)7 : (ushort)9;

            ushort originalLookupType = LookupType;
            if (originalLookupType == extensionLookupType)
                throw new InvalidOperationException("Lookup is already an Extension lookup; SubtableOffset16 overflow cannot be fixed automatically.");

            if (kind == OtlLayoutKind.Gsub)
            {
                if (originalLookupType is 0 or > 8 or 7)
                    throw new InvalidOperationException("Auto-extension is only supported for GSUB lookup types 1–6 and 8.");
            }
            else
            {
                if (originalLookupType is 0 or > 8)
                    throw new InvalidOperationException("Auto-extension is only supported for GPOS lookup types 1–8.");
            }

            int subCount = _subtables.Count;
            int headerLen = checked(6 + (subCount * 2) + (UsesMarkFilteringSet ? 2 : 0));

            // Layout:
            // - Lookup header + subtable Offset16 array (+ optional MarkFilteringSet)
            // - N * ExtensionSubtable headers (8 bytes each) referenced by Offset16
            // - payload subtables appended after headers, referenced via Offset32 from each extension header

            int extHeadersStart = Align2(headerLen);
            int extHeadersLen = checked(subCount * 8);

            if (subCount != 0)
            {
                int lastExtOffset = checked(extHeadersStart + ((subCount - 1) * 8));
                if (lastExtOffset > ushort.MaxValue)
                    throw new InvalidOperationException("SubtableOffset16 overflow: too many subtables to fix using extension headers.");
            }

            int payloadStart = Align2(checked(extHeadersStart + extHeadersLen));

            Span<int> payloadOffsets = subCount <= 128 ? stackalloc int[subCount] : new int[subCount];

            int pos = payloadStart;
            for (int i = 0; i < subCount; i++)
            {
                pos = Align2(pos);
                payloadOffsets[i] = pos;
                pos = checked(pos + _subtables[i].Length);
            }

            byte[] bytes = new byte[pos];
            var span = bytes.AsSpan();

            // Lookup header (note the LookupType becomes the extension type).
            BigEndian.WriteUInt16(span, 0, extensionLookupType);
            BigEndian.WriteUInt16(span, 2, LookupFlag);
            BigEndian.WriteUInt16(span, 4, checked((ushort)subCount));

            int o = 6;
            for (int i = 0; i < subCount; i++)
            {
                int extOffset = checked(extHeadersStart + (i * 8));
                BigEndian.WriteUInt16(span, o, checked((ushort)extOffset));
                o += 2;
            }

            if (UsesMarkFilteringSet)
                BigEndian.WriteUInt16(span, o, MarkFilteringSet);

            // Extension headers and payloads.
            for (int i = 0; i < subCount; i++)
            {
                int extOffset = checked(extHeadersStart + (i * 8));
                int payloadOffset = payloadOffsets[i];

                long rel = (long)payloadOffset - extOffset;
                if (rel < 8 || rel > uint.MaxValue)
                    throw new InvalidOperationException("Invalid extension payload offset.");

                // format=1, ExtensionLookupType=originalLookupType, ExtensionOffset=Offset32 to payload
                BigEndian.WriteUInt16(span, extOffset + 0, 1);
                BigEndian.WriteUInt16(span, extOffset + 2, originalLookupType);
                BigEndian.WriteUInt32(span, extOffset + 4, checked((uint)rel));

                _subtables[i].Span.CopyTo(span.Slice(payloadOffset, _subtables[i].Length));
            }

            return bytes;
        }
    }
}
