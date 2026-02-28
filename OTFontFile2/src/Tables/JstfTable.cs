using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("JSTF", 6)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("ScriptCount", OtFieldKind.UInt16, 4)]
[OtTagOffsetRecordArray("Script", 6, SubTableType = typeof(JstfScript))]
public readonly partial struct JstfTable
{
    [OtSubTable(6)]
    [OtField("ExtenderGlyphOffset", OtFieldKind.UInt16, 0)]
    [OtField("DefaultLangSysOffset", OtFieldKind.UInt16, 2)]
    [OtField("LangSysCount", OtFieldKind.UInt16, 4)]
    [OtTagOffsetRecordArray("LangSys", 6, SubTableType = typeof(JstfLangSys), OutParameterName = "langSys")]
    [OtSubTableOffset("ExtenderGlyph", nameof(ExtenderGlyphOffset), typeof(ExtenderGlyphs), OutParameterName = "extenderGlyphs")]
    [OtSubTableOffset("DefaultLangSys", nameof(DefaultLangSysOffset), typeof(JstfLangSys), OutParameterName = "langSys")]
    public readonly partial struct JstfScript
    {
    }

    [OtSubTable(2)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("GlyphId", 2, CountPropertyName = "GlyphCount")]
    public readonly partial struct ExtenderGlyphs
    {
    }

    [OtSubTable(2)]
    [OtField("PriorityCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("PriorityOffset", 2, CountPropertyName = "PriorityCount")]
    [OtSubTableOffsetArray("Priority", "PriorityOffset", typeof(JstfPriority))]
    public readonly partial struct JstfLangSys
    {
    }

    [OtSubTable(20)]
    [OtField("ShrinkageEnableGsubOffset", OtFieldKind.UInt16, 0)]
    [OtField("ShrinkageDisableGsubOffset", OtFieldKind.UInt16, 2)]
    [OtField("ShrinkageEnableGposOffset", OtFieldKind.UInt16, 4)]
    [OtField("ShrinkageDisableGposOffset", OtFieldKind.UInt16, 6)]
    [OtField("ShrinkageJstfMaxOffset", OtFieldKind.UInt16, 8)]
    [OtField("ExtensionEnableGsubOffset", OtFieldKind.UInt16, 10)]
    [OtField("ExtensionDisableGsubOffset", OtFieldKind.UInt16, 12)]
    [OtField("ExtensionEnableGposOffset", OtFieldKind.UInt16, 14)]
    [OtField("ExtensionDisableGposOffset", OtFieldKind.UInt16, 16)]
    [OtField("ExtensionJstfMaxOffset", OtFieldKind.UInt16, 18)]
    public readonly partial struct JstfPriority
    {
        public bool TryGetShrinkageEnableGsub(out JstfGsubModList list) => TryGetGsubModList(ShrinkageEnableGsubOffset, out list);
        public bool TryGetShrinkageDisableGsub(out JstfGsubModList list) => TryGetGsubModList(ShrinkageDisableGsubOffset, out list);
        public bool TryGetExtensionEnableGsub(out JstfGsubModList list) => TryGetGsubModList(ExtensionEnableGsubOffset, out list);
        public bool TryGetExtensionDisableGsub(out JstfGsubModList list) => TryGetGsubModList(ExtensionDisableGsubOffset, out list);

        public bool TryGetShrinkageEnableGpos(out JstfGposModList list) => TryGetGposModList(ShrinkageEnableGposOffset, out list);
        public bool TryGetShrinkageDisableGpos(out JstfGposModList list) => TryGetGposModList(ShrinkageDisableGposOffset, out list);
        public bool TryGetExtensionEnableGpos(out JstfGposModList list) => TryGetGposModList(ExtensionEnableGposOffset, out list);
        public bool TryGetExtensionDisableGpos(out JstfGposModList list) => TryGetGposModList(ExtensionDisableGposOffset, out list);

        public bool TryGetShrinkageJstfMax(out JstfMax max) => TryGetMax(ShrinkageJstfMaxOffset, out max);
        public bool TryGetExtensionJstfMax(out JstfMax max) => TryGetMax(ExtensionJstfMaxOffset, out max);

        private bool TryGetGsubModList(ushort rel, out JstfGsubModList list)
        {
            list = default;

            int offset = TryGetRelOffset(rel);
            if (offset == 0)
                return false;
            return JstfGsubModList.TryCreate(_table, offset, out list);
        }

        private bool TryGetGposModList(ushort rel, out JstfGposModList list)
        {
            list = default;

            int offset = TryGetRelOffset(rel);
            if (offset == 0)
                return false;
            return JstfGposModList.TryCreate(_table, offset, out list);
        }

        private bool TryGetMax(ushort rel, out JstfMax max)
        {
            max = default;

            int offset = TryGetRelOffset(rel);
            if (offset == 0)
                return false;
            return JstfMax.TryCreate(_table, offset, out max);
        }

        private int TryGetRelOffset(ushort rel)
        {
            if (rel == 0)
                return 0;

            int offset = _offset + rel;
            return offset <= _offset ? 0 : offset;
        }
    }

    [OtSubTable(2)]
    [OtField("LookupCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LookupIndex", 2, CountPropertyName = "LookupCount")]
    public readonly partial struct JstfGsubModList
    {
    }

    [OtSubTable(2)]
    [OtField("LookupCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LookupIndex", 2, CountPropertyName = "LookupCount")]
    public readonly partial struct JstfGposModList
    {
    }

    [OtSubTable(2)]
    [OtField("LookupCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("LookupOffset", 2, CountPropertyName = "LookupCount")]
    [OtSubTableOffsetArray("Lookup", "LookupOffset", typeof(OtlLayoutTable.Lookup))]
    public readonly partial struct JstfMax
    {
    }
}
