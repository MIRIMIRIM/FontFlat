using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GSUB lookup type 5: Contextual Substitution Subtable (formats 1–3).
/// </summary>
[OtSubTable(2)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtDiscriminant(nameof(SubstFormat))]
[OtCase(1, typeof(GsubContextSubstSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GsubContextSubstSubtable.Format2), Name = "Format2")]
[OtCase(3, typeof(GsubContextSubstSubtable.Format3), Name = "Format3")]
public readonly partial struct GsubContextSubstSubtable
{
    [OtSubTable(6)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("SubRuleSetCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("SubRuleSetOffset", 6, CountPropertyName = "SubRuleSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffsetArray("SubRuleSet", "SubRuleSetOffset", typeof(SubRuleSet))]
    public readonly partial struct Format1
    {
        [OtSubTable(2)]
        [OtField("SubRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("SubRuleOffset", 2, CountPropertyName = "SubRuleCount")]
        [OtSubTableOffsetArray("SubRule", "SubRuleOffset", typeof(SubRule))]
        public readonly partial struct SubRuleSet
        {
            [OtSubTable(4)]
            [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
            [OtField("SubstCount", OtFieldKind.UInt16, 2)]
            [OtUInt16Array("InputGlyphId", 4, CountPropertyName = "GlyphCount", CountAdjustment = -1)]
            [OtSequentialRecordArray(
                "SubstLookupRecord",
                0,
                4,
                CountPropertyName = nameof(SubstLookupRecordCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "4 + ((GlyphCount - 1) * 2)")]
            public readonly partial struct SubRule
            {
                private uint SubstLookupRecordCount => GlyphCount == 0 ? 0u : SubstCount;

                public bool TryGetInputGlyphIdCount(out ushort count)
                {
                    count = 0;

                    ushort glyphCount = GlyphCount;
                    if (glyphCount == 0)
                        return false;

                    count = (ushort)(glyphCount - 1);
                    return true;
                }
            }
        }
    }

    [OtSubTable(8)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("ClassDefOffset", OtFieldKind.UInt16, 4)]
    [OtField("SubClassSetCount", OtFieldKind.UInt16, 6)]
    [OtUInt16Array("SubClassSetOffset", 8, CountPropertyName = "SubClassSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffset("ClassDef", nameof(ClassDefOffset), typeof(ClassDefTable))]
    [OtSubTableOffsetArray("SubClassSet", "SubClassSetOffset", typeof(SubClassSet))]
    public readonly partial struct Format2
    {
        [OtSubTable(2)]
        [OtField("SubClassRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("SubClassRuleOffset", 2, CountPropertyName = "SubClassRuleCount")]
        [OtSubTableOffsetArray("SubClassRule", "SubClassRuleOffset", typeof(SubClassRule), OutParameterName = "rule")]
        public readonly partial struct SubClassSet
        {
            [OtSubTable(4)]
            [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
            [OtField("SubstCount", OtFieldKind.UInt16, 2)]
            [OtUInt16Array("InputClass", 4, CountPropertyName = "GlyphCount", CountAdjustment = -1)]
            [OtSequentialRecordArray(
                "SubstLookupRecord",
                0,
                4,
                CountPropertyName = nameof(SubstLookupRecordCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "4 + ((GlyphCount - 1) * 2)")]
            public readonly partial struct SubClassRule
            {
                private uint SubstLookupRecordCount => GlyphCount == 0 ? 0u : SubstCount;

                public bool TryGetInputClassCount(out ushort count)
                {
                    count = 0;

                    ushort glyphCount = GlyphCount;
                    if (glyphCount == 0)
                        return false;

                    count = (ushort)(glyphCount - 1);
                    return true;
                }
            }
        }
    }

    [OtSubTable(6)]
    [OtField("GlyphCount", OtFieldKind.UInt16, 2)]
    [OtField("SubstCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("CoverageOffset", 6, CountPropertyName = "GlyphCount")]
    [OtSubTableOffsetArray("Coverage", "CoverageOffset", typeof(CoverageTable))]
    [OtSequentialRecordArray(
        "SubstLookupRecord",
        0,
        4,
        CountPropertyName = nameof(SubstCount),
        RecordTypeName = nameof(SequenceLookupRecord),
        OutParameterName = "record",
        RecordsOffsetExpression = "6 + (GlyphCount * 2)")]
    public readonly partial struct Format3
    {
    }
}
