namespace OTFontFile2.Tables;

public readonly partial struct OtlLayoutTable
{
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

        if (!TryGetFeatureList(out var featureList))
            return false;

        enumerator = new LookupIndexEnumerator(featureList, langSys, enabledFeatures);
        return true;
    }

    public ref struct LookupIndexEnumerator
    {
        private readonly FeatureList _featureList;
        private readonly LangSys _langSys;
        private readonly ReadOnlySpan<Tag> _enabledFeatures;
        private readonly bool _allFeatures;
        private readonly ushort _requiredFeatureIndex;

        private bool _requiredPending;
        private int _featureIndexPos;

        private Feature _currentFeature;
        private int _currentLookupPos;
        private bool _inFeature;

        internal LookupIndexEnumerator(FeatureList featureList, LangSys langSys, ReadOnlySpan<Tag> enabledFeatures)
        {
            _featureList = featureList;
            _langSys = langSys;
            _enabledFeatures = enabledFeatures;
            _allFeatures = enabledFeatures.Length == 0;
            _requiredFeatureIndex = langSys.RequiredFeatureIndex;

            _requiredPending = _requiredFeatureIndex != 0xFFFF;
            _featureIndexPos = 0;

            _currentFeature = default;
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
                    if (_currentLookupPos >= _currentFeature.LookupIndexCount)
                    {
                        _inFeature = false;
                        continue;
                    }

                    if (!_currentFeature.TryGetLookupListIndex(_currentLookupPos++, out ushort lookupIndex))
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

            if (!_featureList.TryGetFeatureRecord(featureIndex, out var rec))
                return false;

            if (!ignoreFeatureFilter && !_allFeatures && !Contains(_enabledFeatures, rec.FeatureTag))
                return true; // filtered out

            if (rec.FeatureOffset == 0)
                return true; // skip missing feature table

            if (!_featureList.TryGetFeature(rec, out var feature))
                return false;

            _currentFeature = feature;
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
