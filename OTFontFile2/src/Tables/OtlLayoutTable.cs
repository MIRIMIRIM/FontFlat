using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// OpenType Layout common table format used by GSUB and GPOS.
/// Provides navigation for ScriptList/FeatureList/LookupList.
/// </summary>
[OtSubTable(10, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("ScriptListOffset", OtFieldKind.UInt16, 4)]
[OtField("FeatureListOffset", OtFieldKind.UInt16, 6)]
[OtField("LookupListOffset", OtFieldKind.UInt16, 8)]
[OtSubTableOffset("ScriptList", nameof(ScriptListOffset), typeof(ScriptList))]
[OtSubTableOffset("FeatureList", nameof(FeatureListOffset), typeof(FeatureList))]
[OtSubTableOffset("LookupList", nameof(LookupListOffset), typeof(LookupList))]
public readonly partial struct OtlLayoutTable
{
    public static bool TryCreate(TableSlice table, out OtlLayoutTable layout)
    {
        // version(4) + 3 offsets(2 each)
        if (table.Length < 10)
        {
            layout = default;
            return false;
        }

        layout = new OtlLayoutTable(table, 0);
        return true;
    }

    public bool HasFeatureVariations => Version.RawValue >= 0x00010001u && _table.Length >= 14;
    public uint FeatureVariationsOffset => HasFeatureVariations ? BigEndian.ReadUInt32(_table.Span, 10) : 0;

    [OtSubTable(2)]
    [OtField("ScriptCount", OtFieldKind.UInt16, 0)]
    [OtTagOffsetRecordArray("Script", 2, SubTableType = typeof(Script))]
    public readonly partial struct ScriptList
    {
    }

    [OtSubTable(4)]
    [OtField("DefaultLangSysOffset", OtFieldKind.UInt16, 0)]
    [OtField("LangSysCount", OtFieldKind.UInt16, 2)]
    [OtTagOffsetRecordArray("LangSys", 4, SubTableType = typeof(LangSys), OutParameterName = "langSys")]
    [OtSubTableOffset("DefaultLangSys", nameof(DefaultLangSysOffset), typeof(LangSys), OutParameterName = "langSys")]
    public readonly partial struct Script
    {
    }

    [OtSubTable(6)]
    [OtField("LookupOrder", OtFieldKind.UInt16, 0)]
    [OtField("RequiredFeatureIndex", OtFieldKind.UInt16, 2)]
    [OtField("FeatureIndexCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("FeatureIndex", 6)]
    public readonly partial struct LangSys
    {
    }

    [OtSubTable(2)]
    [OtField("FeatureCount", OtFieldKind.UInt16, 0)]
    [OtTagOffsetRecordArray("Feature", 2, SubTableType = typeof(Feature))]
    public readonly partial struct FeatureList
    {
    }

    [OtSubTable(4)]
    [OtField("FeatureParamsOffset", OtFieldKind.UInt16, 0)]
    [OtField("LookupIndexCount", OtFieldKind.UInt16, 2)]
    [OtUInt16Array("LookupListIndex", 4, CountPropertyName = "LookupIndexCount")]
    public readonly partial struct Feature
    {
    }

    [OtSubTable(2)]
    [OtField("LookupCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LookupOffset", 2, CountPropertyName = "LookupCount")]
    [OtSubTableOffsetArray("Lookup", "LookupOffset", typeof(Lookup))]
    public readonly partial struct LookupList
    {
    }

    [OtSubTable(6)]
    [OtField("LookupType", OtFieldKind.UInt16, 0)]
    [OtField("LookupFlag", OtFieldKind.UInt16, 2)]
    [OtField("SubtableCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("SubtableOffset", 6, CountPropertyName = "SubtableCount")]
    public readonly partial struct Lookup
    {
        public const ushort UseMarkFilteringSetFlag = 0x0010;

        public bool UsesMarkFilteringSet => (LookupFlag & UseMarkFilteringSetFlag) != 0;

        public bool TryGetMarkFilteringSet(out ushort markFilteringSet)
        {
            markFilteringSet = 0;

            if (!UsesMarkFilteringSet)
                return false;

            int offset = _offset + 6 + (SubtableCount * 2);
            if ((uint)offset > (uint)_table.Length - 2)
                return false;

            markFilteringSet = BigEndian.ReadUInt16(_table.Span, offset);
            return true;
        }
    }
}
