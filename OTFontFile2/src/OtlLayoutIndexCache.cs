using OTFontFile2.Tables;

namespace OTFontFile2;

/// <summary>
/// Optional decoded cache of the GSUB/GPOS index layer (ScriptList/FeatureList/LookupList).
/// This is intended for tooling and higher-level drivers that repeatedly query scripts/features/lookups.
/// </summary>
public sealed class OtlLayoutIndexCache
{
    private static readonly Tag DfltScriptTag = new(0x44464C54u); // 'DFLT'
    private static readonly Tag DfltLangSysTag = new(0x64666C74u); // 'dflt' (conventional)

    private readonly ScriptEntry[] _scripts;
    private readonly FeatureEntry[] _features;
    private readonly LookupEntry[] _lookups;

    private OtlLayoutIndexCache(ScriptEntry[] scripts, FeatureEntry[] features, LookupEntry[] lookups)
    {
        _scripts = scripts;
        _features = features;
        _lookups = lookups;
    }

    public int ScriptCount => _scripts.Length;
    public int FeatureCount => _features.Length;
    public int LookupCount => _lookups.Length;

    public static bool TryCreate(OtlLayoutTable layout, out OtlLayoutIndexCache cache)
    {
        cache = null!;

        if (!layout.TryGetScriptList(out var scripts) ||
            !layout.TryGetFeatureList(out var features) ||
            !layout.TryGetLookupList(out var lookups))
        {
            return false;
        }

        return TryCreate(scripts, features, lookups, out cache);
    }

    public static bool TryCreate(in GsubTable gsub, out OtlLayoutIndexCache cache)
    {
        cache = null!;

        if (!gsub.TryGetScriptList(out var scripts) ||
            !gsub.TryGetFeatureList(out var features) ||
            !gsub.TryGetLookupList(out var lookups))
        {
            return false;
        }

        return TryCreate(scripts, features, lookups, out cache);
    }

    public static bool TryCreate(in GposTable gpos, out OtlLayoutIndexCache cache)
    {
        cache = null!;

        if (!gpos.TryGetScriptList(out var scripts) ||
            !gpos.TryGetFeatureList(out var features) ||
            !gpos.TryGetLookupList(out var lookups))
        {
            return false;
        }

        return TryCreate(scripts, features, lookups, out cache);
    }

    private static bool TryCreate(
        OtlLayoutTable.ScriptList scriptList,
        OtlLayoutTable.FeatureList featureList,
        OtlLayoutTable.LookupList lookupList,
        out OtlLayoutIndexCache cache)
    {
        cache = null!;

        if (!TryDecodeLookups(lookupList, out var lookups))
            return false;

        if (!TryDecodeFeatures(featureList, out var features))
            return false;

        if (!TryDecodeScripts(scriptList, out var scripts))
            return false;

        cache = new OtlLayoutIndexCache(scripts, features, lookups);
        return true;
    }

    public bool TryFindScript(Tag scriptTag, out ScriptEntry script)
        => TryFindScriptByTag(scriptTag, out script);

    public bool TryFindScriptOrDefault(Tag scriptTag, out ScriptEntry script)
    {
        if (TryFindScriptByTag(scriptTag, out script))
            return true;

        return TryFindScriptByTag(DfltScriptTag, out script);
    }

    public bool TryGetLookup(int lookupIndex, out LookupEntry lookup)
    {
        lookup = default;

        if ((uint)lookupIndex >= (uint)_lookups.Length)
            return false;

        lookup = _lookups[lookupIndex];
        return true;
    }

    public bool TryGetFeature(int featureIndex, out FeatureEntry feature)
    {
        feature = default;

        if ((uint)featureIndex >= (uint)_features.Length)
            return false;

        feature = _features[featureIndex];
        return true;
    }

    /// <summary>
    /// Returns an enumerator of lookup indices enabled by the given script/langsys and feature filter.
    /// The required feature (if present) is always included and is enumerated first.
    /// </summary>
    public bool TryGetLookupIndexEnumerator(
        Tag scriptTag,
        Tag langSysTag,
        ReadOnlySpan<Tag> enabledFeatures,
        out LookupIndexEnumerator enumerator)
    {
        enumerator = default;

        if (!TryFindScriptOrDefault(scriptTag, out var script))
            return false;

        if (!script.TryFindLangSysOrDefault(langSysTag, out var langSys))
            return false;

        enumerator = new LookupIndexEnumerator(_features, _lookups.Length, langSys, enabledFeatures);
        return true;
    }

    private bool TryFindScriptByTag(Tag scriptTag, out ScriptEntry script)
    {
        script = default;

        int lo = 0;
        int hi = _scripts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            Tag midTag = _scripts[mid].ScriptTag;
            int cmp = midTag.CompareTo(scriptTag);
            if (cmp < 0)
            {
                lo = mid + 1;
                continue;
            }
            if (cmp > 0)
            {
                hi = mid - 1;
                continue;
            }

            script = _scripts[mid];
            return true;
        }

        return false;
    }

    private static bool TryDecodeLookups(OtlLayoutTable.LookupList lookupList, out LookupEntry[] lookups)
    {
        lookups = Array.Empty<LookupEntry>();

        int count = lookupList.LookupCount;
        if (count == 0)
            return true;

        var decoded = new LookupEntry[count];
        for (int i = 0; i < count; i++)
        {
            if (!lookupList.TryGetLookup(i, out var lookup))
                return false;

            ushort markFilteringSet = 0;
            bool hasMarkFilteringSet = lookup.TryGetMarkFilteringSet(out markFilteringSet);

            decoded[i] = new LookupEntry(
                lookupType: lookup.LookupType,
                lookupFlag: lookup.LookupFlag,
                subtableCount: lookup.SubtableCount,
                hasMarkFilteringSet: hasMarkFilteringSet,
                markFilteringSet: markFilteringSet);
        }

        lookups = decoded;
        return true;
    }

    private static bool TryDecodeFeatures(OtlLayoutTable.FeatureList featureList, out FeatureEntry[] features)
    {
        features = Array.Empty<FeatureEntry>();

        int count = featureList.FeatureCount;
        if (count == 0)
            return true;

        var decoded = new FeatureEntry[count];
        for (int i = 0; i < count; i++)
        {
            if (!featureList.TryGetFeatureRecord(i, out var rec))
                return false;

            Tag featureTag = rec.FeatureTag;

            if (rec.FeatureOffset == 0)
            {
                decoded[i] = new FeatureEntry(featureTag, featureParamsOffset: 0, lookupIndices: Array.Empty<ushort>());
                continue;
            }

            if (!featureList.TryGetFeature(rec, out var feature))
                return false;

            ushort lookupIndexCount = feature.LookupIndexCount;
            ushort[] lookupIndices;
            if (lookupIndexCount == 0)
            {
                lookupIndices = Array.Empty<ushort>();
            }
            else
            {
                lookupIndices = new ushort[lookupIndexCount];
                for (int j = 0; j < lookupIndexCount; j++)
                {
                    if (!feature.TryGetLookupListIndex(j, out ushort lookupIndex))
                        return false;

                    lookupIndices[j] = lookupIndex;
                }
            }

            decoded[i] = new FeatureEntry(featureTag, feature.FeatureParamsOffset, lookupIndices);
        }

        features = decoded;
        return true;
    }

    private static bool TryDecodeScripts(OtlLayoutTable.ScriptList scriptList, out ScriptEntry[] scripts)
    {
        scripts = Array.Empty<ScriptEntry>();

        int scriptCount = scriptList.ScriptCount;
        if (scriptCount == 0)
            return true;

        var decoded = new ScriptEntry[scriptCount];

        int outCount = 0;
        for (int i = 0; i < scriptCount; i++)
        {
            if (!scriptList.TryGetScriptRecord(i, out var rec))
                return false;

            if (rec.ScriptOffset == 0)
                continue;

            if (!scriptList.TryGetScript(rec, out var script))
                return false;

            if (!TryDecodeScript(rec.ScriptTag, script, out var scriptEntry))
                return false;

            decoded[outCount++] = scriptEntry;
        }

        if (outCount != decoded.Length)
        {
            if (outCount == 0)
            {
                scripts = Array.Empty<ScriptEntry>();
                return true;
            }

            var trimmed = new ScriptEntry[outCount];
            Array.Copy(decoded, trimmed, outCount);
            decoded = trimmed;
        }

        Array.Sort(decoded, static (a, b) => a.ScriptTag.CompareTo(b.ScriptTag));
        scripts = decoded;
        return true;
    }

    private static bool TryDecodeScript(Tag scriptTag, OtlLayoutTable.Script script, out ScriptEntry entry)
    {
        entry = default;

        bool hasDefault = false;
        LangSysEntry defaultLangSys = default;
        if (script.TryGetDefaultLangSys(out var dflt))
        {
            if (!TryDecodeLangSys(DfltLangSysTag, dflt, out defaultLangSys))
                return false;
            hasDefault = true;
        }

        int langCount = script.LangSysCount;
        LangSysEntry[] langSys;
        if (langCount == 0)
        {
            langSys = Array.Empty<LangSysEntry>();
        }
        else
        {
            langSys = new LangSysEntry[langCount];
            int outCount = 0;
            for (int i = 0; i < langCount; i++)
            {
                if (!script.TryGetLangSysRecord(i, out var rec))
                    return false;

                if (rec.LangSysOffset == 0)
                    continue;

                if (!script.TryGetLangSys(rec, out var ls))
                    return false;

                if (!TryDecodeLangSys(rec.LangSysTag, ls, out var lsEntry))
                    return false;

                langSys[outCount++] = lsEntry;
            }

            if (outCount != langSys.Length)
            {
                if (outCount == 0)
                {
                    langSys = Array.Empty<LangSysEntry>();
                }
                else
                {
                    var trimmed = new LangSysEntry[outCount];
                    Array.Copy(langSys, trimmed, outCount);
                    langSys = trimmed;
                }
            }

            Array.Sort(langSys, static (a, b) => a.LangSysTag.CompareTo(b.LangSysTag));
        }

        entry = new ScriptEntry(scriptTag, hasDefault, defaultLangSys, langSys);
        return true;
    }

    private static bool TryDecodeLangSys(Tag langSysTag, OtlLayoutTable.LangSys langSys, out LangSysEntry entry)
    {
        entry = default;

        ushort required = langSys.RequiredFeatureIndex;
        ushort featureCount = langSys.FeatureIndexCount;

        ushort[] featureIndices;
        if (featureCount == 0)
        {
            featureIndices = Array.Empty<ushort>();
        }
        else
        {
            featureIndices = new ushort[featureCount];
            for (int i = 0; i < featureCount; i++)
            {
                if (!langSys.TryGetFeatureIndex(i, out ushort featureIndex))
                    return false;

                featureIndices[i] = featureIndex;
            }
        }

        entry = new LangSysEntry(langSysTag, required, featureIndices);
        return true;
    }

    public readonly struct ScriptEntry
    {
        private readonly LangSysEntry[] _langSys;

        internal ScriptEntry(Tag scriptTag, bool hasDefaultLangSys, LangSysEntry defaultLangSys, LangSysEntry[] langSys)
        {
            ScriptTag = scriptTag;
            HasDefaultLangSys = hasDefaultLangSys;
            DefaultLangSys = defaultLangSys;
            _langSys = langSys;
        }

        public Tag ScriptTag { get; }

        public bool HasDefaultLangSys { get; }
        public LangSysEntry DefaultLangSys { get; }

        public int LangSysCount => _langSys.Length;

        public bool TryGetLangSys(int index, out LangSysEntry langSys)
        {
            langSys = default;
            if ((uint)index >= (uint)_langSys.Length)
                return false;
            langSys = _langSys[index];
            return true;
        }

        public bool TryFindLangSys(Tag langSysTag, out LangSysEntry langSys)
        {
            langSys = default;

            int lo = 0;
            int hi = _langSys.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                Tag midTag = _langSys[mid].LangSysTag;
                int cmp = midTag.CompareTo(langSysTag);
                if (cmp < 0)
                {
                    lo = mid + 1;
                    continue;
                }
                if (cmp > 0)
                {
                    hi = mid - 1;
                    continue;
                }

                langSys = _langSys[mid];
                return true;
            }

            return false;
        }

        public bool TryFindLangSysOrDefault(Tag langSysTag, out LangSysEntry langSys)
        {
            if (TryFindLangSys(langSysTag, out langSys))
                return true;

            if (HasDefaultLangSys)
            {
                langSys = DefaultLangSys;
                return true;
            }

            langSys = default;
            return false;
        }
    }

    public readonly struct LangSysEntry
    {
        private readonly ushort[] _featureIndices;

        internal LangSysEntry(Tag langSysTag, ushort requiredFeatureIndex, ushort[] featureIndices)
        {
            LangSysTag = langSysTag;
            RequiredFeatureIndex = requiredFeatureIndex;
            _featureIndices = featureIndices;
        }

        public Tag LangSysTag { get; }

        public ushort RequiredFeatureIndex { get; }

        public int FeatureIndexCount => _featureIndices.Length;

        public bool TryGetFeatureIndex(int index, out ushort featureIndex)
        {
            featureIndex = 0;
            if ((uint)index >= (uint)_featureIndices.Length)
                return false;
            featureIndex = _featureIndices[index];
            return true;
        }
    }

    public readonly struct FeatureEntry
    {
        private readonly ushort[] _lookupIndices;

        internal FeatureEntry(Tag featureTag, ushort featureParamsOffset, ushort[] lookupIndices)
        {
            FeatureTag = featureTag;
            FeatureParamsOffset = featureParamsOffset;
            _lookupIndices = lookupIndices;
        }

        public Tag FeatureTag { get; }

        public ushort FeatureParamsOffset { get; }

        public int LookupIndexCount => _lookupIndices.Length;

        internal ushort[] LookupIndices => _lookupIndices;

        public bool TryGetLookupIndex(int index, out ushort lookupIndex)
        {
            lookupIndex = 0;
            if ((uint)index >= (uint)_lookupIndices.Length)
                return false;
            lookupIndex = _lookupIndices[index];
            return true;
        }
    }

    public readonly struct LookupEntry
    {
        internal LookupEntry(ushort lookupType, ushort lookupFlag, ushort subtableCount, bool hasMarkFilteringSet, ushort markFilteringSet)
        {
            LookupType = lookupType;
            LookupFlag = lookupFlag;
            SubtableCount = subtableCount;
            HasMarkFilteringSet = hasMarkFilteringSet;
            MarkFilteringSet = markFilteringSet;
        }

        public ushort LookupType { get; }
        public ushort LookupFlag { get; }
        public ushort SubtableCount { get; }

        public bool HasMarkFilteringSet { get; }
        public ushort MarkFilteringSet { get; }
    }

    public ref struct LookupIndexEnumerator
    {
        private readonly FeatureEntry[] _features;
        private readonly int _lookupCount;
        private readonly LangSysEntry _langSys;
        private readonly ReadOnlySpan<Tag> _enabledFeatures;
        private readonly bool _allFeatures;
        private readonly ushort _requiredFeatureIndex;

        private bool _requiredPending;
        private int _featureIndexPos;

        private ushort[] _currentLookups;
        private int _currentLookupPos;
        private bool _inFeature;

        internal LookupIndexEnumerator(FeatureEntry[] features, int lookupCount, LangSysEntry langSys, ReadOnlySpan<Tag> enabledFeatures)
        {
            _features = features;
            _lookupCount = lookupCount;
            _langSys = langSys;
            _enabledFeatures = enabledFeatures;
            _allFeatures = enabledFeatures.Length == 0;
            _requiredFeatureIndex = langSys.RequiredFeatureIndex;

            _requiredPending = _requiredFeatureIndex != 0xFFFF;
            _featureIndexPos = 0;

            _currentLookups = Array.Empty<ushort>();
            _currentLookupPos = 0;
            _inFeature = false;

            Current = 0;
        }

        public ushort Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                if (_inFeature)
                {
                    if (_currentLookupPos >= _currentLookups.Length)
                    {
                        _inFeature = false;
                        continue;
                    }

                    ushort lookupIndex = _currentLookups[_currentLookupPos++];
                    if (lookupIndex >= _lookupCount)
                        return false;

                    Current = lookupIndex;
                    return true;
                }

                if (_requiredPending)
                {
                    _requiredPending = false;
                    if (!TryStartFeatureByIndex(_requiredFeatureIndex, ignoreFeatureFilter: true, out _))
                        return false;
                    continue;
                }

                if (_featureIndexPos >= _langSys.FeatureIndexCount)
                    return false;

                if (!_langSys.TryGetFeatureIndex(_featureIndexPos++, out ushort featureIndex))
                    return false;

                if (_requiredFeatureIndex != 0xFFFF && featureIndex == _requiredFeatureIndex)
                    continue;

                if (!TryStartFeatureByIndex(featureIndex, ignoreFeatureFilter: false, out bool startedFeature))
                    return false;

                if (!startedFeature)
                    continue;
            }
        }

        private bool TryStartFeatureByIndex(ushort featureIndex, bool ignoreFeatureFilter, out bool started)
        {
            started = false;

            if (featureIndex >= _features.Length)
                return false;

            FeatureEntry entry = _features[featureIndex];

            if (!ignoreFeatureFilter && !_allFeatures && !Contains(_enabledFeatures, entry.FeatureTag))
                return true;

            if (entry.LookupIndexCount == 0)
                return true;

            _currentLookups = entry.LookupIndices;
            _currentLookupPos = 0;
            _inFeature = true;
            started = true;
            return true;
        }

        private static bool Contains(ReadOnlySpan<Tag> tags, Tag tag)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }
    }
}
