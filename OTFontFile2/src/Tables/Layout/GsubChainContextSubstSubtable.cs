using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GSUB lookup type 6: Chaining Contextual Substitution Subtable (formats 1–3).
/// </summary>
[OtSubTable(2)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtDiscriminant(nameof(SubstFormat))]
[OtCase(1, typeof(GsubChainContextSubstSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GsubChainContextSubstSubtable.Format2), Name = "Format2")]
[OtCase(3, typeof(GsubChainContextSubstSubtable.Format3), Name = "Format3")]
public readonly partial struct GsubChainContextSubstSubtable
{
    [OtSubTable(6)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("ChainSubRuleSetCount", OtFieldKind.UInt16, 4)]
    [OtUInt16Array("ChainSubRuleSetOffset", 6, CountPropertyName = "ChainSubRuleSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffsetArray("ChainSubRuleSet", "ChainSubRuleSetOffset", typeof(ChainSubRuleSet))]
    public readonly partial struct Format1
    {
        [OtSubTable(2)]
        [OtField("ChainSubRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("ChainSubRuleOffset", 2, CountPropertyName = "ChainSubRuleCount")]
        [OtSubTableOffsetArray("ChainSubRule", "ChainSubRuleOffset", typeof(ChainSubRule), OutParameterName = "rule")]
        public readonly partial struct ChainSubRuleSet
        {
            [OtSubTable(2)]
            [OtField("BacktrackGlyphCount", OtFieldKind.UInt16, 0)]
            [OtUInt16Array("BacktrackGlyphId", 2, CountPropertyName = nameof(BacktrackGlyphCount), OutParameterName = "glyphId")]
            [OtUInt16Array(
                "InputGlyphId",
                0,
                CountPropertyName = nameof(InputGlyphCount),
                CountAdjustment = -1,
                OutParameterName = "glyphId",
                ValuesOffsetExpression = "4 + (BacktrackGlyphCount * 2)")]
            [OtUInt16Array(
                "LookaheadGlyphId",
                0,
                CountPropertyName = nameof(LookaheadGlyphCount),
                OutParameterName = "glyphId",
                ValuesOffsetExpression = "6 + (BacktrackGlyphCount * 2) + ((InputGlyphCount > 0 ? InputGlyphCount - 1 : 0) * 2)")]
            [OtSequentialRecordArray(
                "SubstLookupRecord",
                0,
                4,
                CountPropertyName = nameof(SubstCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "8 + (BacktrackGlyphCount * 2) + ((InputGlyphCount > 0 ? InputGlyphCount - 1 : 0) * 2) + (LookaheadGlyphCount * 2)")]
            public readonly partial struct ChainSubRule
            {
                private ushort InputGlyphCount => TryGetInputGlyphCount(out ushort count) ? count : (ushort)0;
                private ushort LookaheadGlyphCount => TryGetLookaheadGlyphCount(out ushort count) ? count : (ushort)0;
                private ushort SubstCount => TryGetSubstCount(out ushort count) ? count : (ushort)0;

                public bool TryGetInputGlyphCount(out ushort count)
                {
                    count = 0;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }

                public bool TryGetLookaheadGlyphCount(out ushort count)
                {
                    count = 0;

                    if (!TryGetInputGlyphCount(out ushort inputCount))
                        return false;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2) + 2 + ((inputCount > 0 ? inputCount - 1 : 0) * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }

                public bool TryGetSubstCount(out ushort count)
                {
                    count = 0;

                    if (!TryGetInputGlyphCount(out ushort inputCount))
                        return false;

                    if (!TryGetLookaheadGlyphCount(out ushort lookaheadCount))
                        return false;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2) + 2 + ((inputCount > 0 ? inputCount - 1 : 0) * 2) + 2 + (lookaheadCount * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }
            }
        }
    }

    [OtSubTable(12)]
    [OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
    [OtField("BacktrackClassDefOffset", OtFieldKind.UInt16, 4)]
    [OtField("InputClassDefOffset", OtFieldKind.UInt16, 6)]
    [OtField("LookaheadClassDefOffset", OtFieldKind.UInt16, 8)]
    [OtField("ChainSubClassSetCount", OtFieldKind.UInt16, 10)]
    [OtUInt16Array("ChainSubClassSetOffset", 12, CountPropertyName = "ChainSubClassSetCount")]
    [OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
    [OtSubTableOffset("BacktrackClassDef", nameof(BacktrackClassDefOffset), typeof(ClassDefTable), OutParameterName = "classDef")]
    [OtSubTableOffset("InputClassDef", nameof(InputClassDefOffset), typeof(ClassDefTable), OutParameterName = "classDef")]
    [OtSubTableOffset("LookaheadClassDef", nameof(LookaheadClassDefOffset), typeof(ClassDefTable), OutParameterName = "classDef")]
    [OtSubTableOffsetArray("ChainSubClassSet", "ChainSubClassSetOffset", typeof(ChainSubClassSet), OutParameterName = "set")]
    public readonly partial struct Format2
    {
        [OtSubTable(2)]
        [OtField("ChainSubClassRuleCount", OtFieldKind.UInt16, 0)]
        [OtUInt16Array("ChainSubClassRuleOffset", 2, CountPropertyName = "ChainSubClassRuleCount")]
        [OtSubTableOffsetArray("ChainSubClassRule", "ChainSubClassRuleOffset", typeof(ChainSubClassRule), OutParameterName = "rule")]
        public readonly partial struct ChainSubClassSet
        {
            [OtSubTable(2)]
            [OtField("BacktrackGlyphCount", OtFieldKind.UInt16, 0)]
            [OtUInt16Array("BacktrackClass", 2, CountPropertyName = nameof(BacktrackGlyphCount), OutParameterName = "classValue")]
            [OtUInt16Array(
                "InputClass",
                0,
                CountPropertyName = nameof(InputGlyphCount),
                CountAdjustment = -1,
                OutParameterName = "classValue",
                ValuesOffsetExpression = "4 + (BacktrackGlyphCount * 2)")]
            [OtUInt16Array(
                "LookaheadClass",
                0,
                CountPropertyName = nameof(LookaheadGlyphCount),
                OutParameterName = "classValue",
                ValuesOffsetExpression = "6 + (BacktrackGlyphCount * 2) + ((InputGlyphCount > 0 ? InputGlyphCount - 1 : 0) * 2)")]
            [OtSequentialRecordArray(
                "SubstLookupRecord",
                0,
                4,
                CountPropertyName = nameof(SubstCount),
                RecordTypeName = nameof(SequenceLookupRecord),
                OutParameterName = "record",
                RecordsOffsetExpression = "8 + (BacktrackGlyphCount * 2) + ((InputGlyphCount > 0 ? InputGlyphCount - 1 : 0) * 2) + (LookaheadGlyphCount * 2)")]
            public readonly partial struct ChainSubClassRule
            {
                private ushort InputGlyphCount => TryGetInputGlyphCount(out ushort count) ? count : (ushort)0;
                private ushort LookaheadGlyphCount => TryGetLookaheadGlyphCount(out ushort count) ? count : (ushort)0;
                private ushort SubstCount => TryGetSubstCount(out ushort count) ? count : (ushort)0;

                public bool TryGetInputGlyphCount(out ushort count)
                {
                    count = 0;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }

                public bool TryGetLookaheadGlyphCount(out ushort count)
                {
                    count = 0;

                    if (!TryGetInputGlyphCount(out ushort inputCount))
                        return false;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2) + 2 + ((inputCount > 0 ? inputCount - 1 : 0) * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }

                public bool TryGetSubstCount(out ushort count)
                {
                    count = 0;

                    if (!TryGetInputGlyphCount(out ushort inputCount))
                        return false;

                    if (!TryGetLookaheadGlyphCount(out ushort lookaheadCount))
                        return false;

                    ushort backCount = BacktrackGlyphCount;
                    int o = _offset + 2 + (backCount * 2) + 2 + ((inputCount > 0 ? inputCount - 1 : 0) * 2) + 2 + (lookaheadCount * 2);
                    if ((uint)o > (uint)_table.Length - 2)
                        return false;

                    count = BigEndian.ReadUInt16(_table.Span, o);
                    return true;
                }
            }
        }
    }

    [OtSubTable(4)]
    [OtField("BacktrackGlyphCount", OtFieldKind.UInt16, 2)]
    [OtUInt16Array("BacktrackCoverageOffset", 4, CountPropertyName = "BacktrackGlyphCount")]
    [OtSubTableOffsetArray("BacktrackCoverage", "BacktrackCoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
    [OtUInt16Array(
        "InputCoverageOffset",
        0,
        CountPropertyName = nameof(InputGlyphCount),
        OutParameterName = "coverageOffset",
        ValuesOffsetExpression = "6 + (BacktrackGlyphCount * 2)")]
    [OtSubTableOffsetArray("InputCoverage", "InputCoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
    [OtUInt16Array(
        "LookaheadCoverageOffset",
        0,
        CountPropertyName = nameof(LookaheadGlyphCount),
        OutParameterName = "coverageOffset",
        ValuesOffsetExpression = "8 + (BacktrackGlyphCount * 2) + (InputGlyphCount * 2)")]
    [OtSubTableOffsetArray("LookaheadCoverage", "LookaheadCoverageOffset", typeof(CoverageTable), OutParameterName = "coverage")]
    [OtSequentialRecordArray(
        "SubstLookupRecord",
        0,
        4,
        CountPropertyName = nameof(SubstCount),
        RecordTypeName = nameof(SequenceLookupRecord),
        OutParameterName = "record",
        RecordsOffsetExpression = "10 + (BacktrackGlyphCount * 2) + (InputGlyphCount * 2) + (LookaheadGlyphCount * 2)")]
    public readonly partial struct Format3
    {
        private ushort InputGlyphCount => TryGetInputGlyphCount(out ushort count) ? count : (ushort)0;
        private ushort LookaheadGlyphCount => TryGetLookaheadGlyphCount(out ushort count) ? count : (ushort)0;
        private ushort SubstCount => TryGetSubstCount(out ushort count) ? count : (ushort)0;

        public bool TryGetInputGlyphCount(out ushort count)
        {
            count = 0;

            ushort backCount = BacktrackGlyphCount;
            int o = _offset + 4 + (backCount * 2);
            if ((uint)o > (uint)_table.Length - 2)
                return false;

            count = BigEndian.ReadUInt16(_table.Span, o);
            return true;
        }

        public bool TryGetLookaheadGlyphCount(out ushort count)
        {
            count = 0;

            if (!TryGetInputGlyphCount(out ushort inputCount))
                return false;

            ushort backCount = BacktrackGlyphCount;
            int o = _offset + 4 + (backCount * 2) + 2 + (inputCount * 2);
            if ((uint)o > (uint)_table.Length - 2)
                return false;

            count = BigEndian.ReadUInt16(_table.Span, o);
            return true;
        }

        public bool TryGetSubstCount(out ushort count)
        {
            count = 0;

            if (!TryGetInputGlyphCount(out ushort inputCount))
                return false;

            if (!TryGetLookaheadGlyphCount(out ushort lookaheadCount))
                return false;

            ushort backCount = BacktrackGlyphCount;
            int o = _offset + 4 + (backCount * 2) + 2 + (inputCount * 2) + 2 + (lookaheadCount * 2);
            if ((uint)o > (uint)_table.Length - 2)
                return false;

            count = BigEndian.ReadUInt16(_table.Span, o);
            return true;
        }
    }
}
