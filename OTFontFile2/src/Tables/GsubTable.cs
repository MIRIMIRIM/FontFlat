using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("GSUB", 10, GenerateTryCreate = false, GenerateStorage = false)]
public readonly partial struct GsubTable
{
    private readonly TableSlice _table;
    private readonly OtlLayoutTable _layout;

    private GsubTable(TableSlice table, OtlLayoutTable layout)
    {
        _table = table;
        _layout = layout;
    }

    public static bool TryCreate(TableSlice table, out GsubTable gsub)
    {
        if (!OtlLayoutTable.TryCreate(table, out var layout))
        {
            gsub = default;
            return false;
        }

        gsub = new GsubTable(table, layout);
        return true;
    }

    public Fixed1616 Version => _layout.Version;
    public ushort ScriptListOffset => _layout.ScriptListOffset;
    public ushort FeatureListOffset => _layout.FeatureListOffset;
    public ushort LookupListOffset => _layout.LookupListOffset;

    public bool HasFeatureVariations => _layout.HasFeatureVariations;
    public uint FeatureVariationsOffset => _layout.FeatureVariationsOffset;

    public bool TryGetScriptList(out OtlLayoutTable.ScriptList scriptList) => _layout.TryGetScriptList(out scriptList);
    public bool TryGetFeatureList(out OtlLayoutTable.FeatureList featureList) => _layout.TryGetFeatureList(out featureList);
    public bool TryGetLookupList(out OtlLayoutTable.LookupList lookupList) => _layout.TryGetLookupList(out lookupList);

    public bool TryGetLookupIndexEnumerator(
        Tag scriptTag,
        Tag langSysTag,
        ReadOnlySpan<Tag> enabledFeatures,
        out OtlLayoutTable.LookupIndexEnumerator enumerator)
        => _layout.TryGetLookupIndexEnumerator(scriptTag, langSysTag, enabledFeatures, out enumerator);
}
