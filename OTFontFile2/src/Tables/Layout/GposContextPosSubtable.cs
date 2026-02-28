using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS lookup type 7: Contextual Positioning Subtable (formats 1–3).
/// </summary>
[OtSubTable(2)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtDiscriminant(nameof(PosFormat))]
[OtCase(1, typeof(GposContextPosSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GposContextPosSubtable.Format2), Name = "Format2")]
[OtCase(3, typeof(GposContextPosSubtable.Format3), Name = "Format3")]
public readonly partial struct GposContextPosSubtable
{
    [OtSubTable(6)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("PosRuleSetCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("PosRuleSetOffset", 6, CountPropertyName = "PosRuleSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffsetArray("PosRuleSet", "PosRuleSetOffset", typeof(PosRuleSet))]
    public readonly partial struct Format1
    {
        [OtSubTable(2)]
        [OtField("PosRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("PosRuleOffset", 2, CountPropertyName = "PosRuleCount")]
        [OtSubTableOffsetArray("PosRule", "PosRuleOffset", typeof(PosRule), OutParameterName = "rule")]
        public readonly partial struct PosRuleSet
        {
            [OtSubTable(4)]
            [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
            [OtField("PosCount", OtFieldKind.UInt16, 2)]
            [OtUInt16Array("InputGlyphId", 4, CountPropertyName = "GlyphCount", CountAdjustment = -1)]
            [OtSequentialRecordArray(
                "PosLookupRecord",
                0,
                4,
                CountPropertyName = nameof(PosLookupRecordCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "4 + ((GlyphCount - 1) * 2)")]
            public readonly partial struct PosRule
            {
                private uint PosLookupRecordCount => GlyphCount == 0 ? 0u : PosCount;

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
    [OtField("PosClassSetCount", OtFieldKind.UInt16, 6)]
    [OtUInt16Array("PosClassSetOffset", 8, CountPropertyName = "PosClassSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffset("ClassDef", nameof(ClassDefOffset), typeof(ClassDefTable))]
    [OtSubTableOffsetArray("PosClassSet", "PosClassSetOffset", typeof(PosClassSet))]
    public readonly partial struct Format2
    {
        [OtSubTable(2)]
        [OtField("PosClassRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("PosClassRuleOffset", 2, CountPropertyName = "PosClassRuleCount")]
        [OtSubTableOffsetArray("PosClassRule", "PosClassRuleOffset", typeof(PosClassRule), OutParameterName = "rule")]
        public readonly partial struct PosClassSet
        {
            [OtSubTable(4)]
            [OtField("GlyphCount", OtFieldKind.UInt16, 0)]
            [OtField("PosCount", OtFieldKind.UInt16, 2)]
            [OtUInt16Array("InputClass", 4, CountPropertyName = "GlyphCount", CountAdjustment = -1)]
            [OtSequentialRecordArray(
                "PosLookupRecord",
                0,
                4,
                CountPropertyName = nameof(PosLookupRecordCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "4 + ((GlyphCount - 1) * 2)")]
            public readonly partial struct PosClassRule
            {
                private uint PosLookupRecordCount => GlyphCount == 0 ? 0u : PosCount;

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
    [OtField("PosCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("CoverageOffset", 6, CountPropertyName = "GlyphCount")]
    [OtSubTableOffsetArray("Coverage", "CoverageOffset", typeof(CoverageTable))]
    [OtSequentialRecordArray(
        "PosLookupRecord",
        0,
        4,
        CountPropertyName = nameof(PosCount),
        RecordTypeName = nameof(SequenceLookupRecord),
        OutParameterName = "record",
        RecordsOffsetExpression = "6 + (GlyphCount * 2)")]
    public readonly partial struct Format3
    {
    }
}
