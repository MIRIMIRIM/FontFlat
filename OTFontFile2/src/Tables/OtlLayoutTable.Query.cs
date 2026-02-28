namespace OTFontFile2.Tables;

public readonly partial struct OtlLayoutTable
{
    private static readonly Tag DfltScriptTag = new(0x44464C54u); // 'DFLT'

    public bool TryFindScript(Tag scriptTag, out Script script)
    {
        script = default;
        return TryGetScriptList(out var scriptList) && scriptList.TryFindScript(scriptTag, out script);
    }

    public bool TryFindScriptOrDefault(Tag scriptTag, out Script script)
    {
        script = default;

        if (!TryGetScriptList(out var scriptList))
            return false;

        if (scriptList.TryFindScript(scriptTag, out script))
            return true;

        return scriptList.TryFindScript(DfltScriptTag, out script);
    }

    public bool TryFindFeature(Tag featureTag, out Feature feature)
    {
        feature = default;
        return TryGetFeatureList(out var featureList) && featureList.TryFindFeature(featureTag, out feature);
    }

    public readonly partial struct Script
    {
        public bool TryFindLangSysOrDefault(Tag langSysTag, out LangSys langSys)
        {
            langSys = default;

            if (TryFindLangSys(langSysTag, out langSys))
                return true;

            return TryGetDefaultLangSys(out langSys);
        }
    }
}

