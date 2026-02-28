using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// OpenType Layout Feature Variations table referenced by GSUB/GPOS version 1.1.
/// </summary>
[OtSubTable(8)]
[OtField("MajorVersion", OtFieldKind.UInt16, 0)]
[OtField("MinorVersion", OtFieldKind.UInt16, 2)]
[OtField("FeatureVariationRecordCount", OtFieldKind.UInt32, 4)]
[OtSequentialRecordArray("FeatureVariationRecord", 8, 8)]
public readonly partial struct FeatureVariationsTable
{
    public readonly struct FeatureVariationRecord
    {
        public uint ConditionSetOffset { get; }
        public uint FeatureTableSubstitutionOffset { get; }

        public FeatureVariationRecord(uint conditionSetOffset, uint featureTableSubstitutionOffset)
        {
            ConditionSetOffset = conditionSetOffset;
            FeatureTableSubstitutionOffset = featureTableSubstitutionOffset;
        }
    }

    public bool TryGetConditionSet(FeatureVariationRecord record, out ConditionSet conditionSet)
    {
        conditionSet = default;

        uint rel = record.ConditionSetOffset;
        if (rel == 0 || rel > int.MaxValue)
            return false;

        int abs = checked(_offset + (int)rel);
        return ConditionSet.TryCreate(_table, abs, out conditionSet);
    }

    public bool TryGetFeatureTableSubstitution(FeatureVariationRecord record, out FeatureTableSubstitution substitution)
    {
        substitution = default;

        uint rel = record.FeatureTableSubstitutionOffset;
        if (rel == 0 || rel > int.MaxValue)
            return false;

        int abs = checked(_offset + (int)rel);
        return FeatureTableSubstitution.TryCreate(_table, abs, out substitution);
    }

    [OtSubTable(2)]
    [OtField("ConditionCount", OtFieldKind.UInt16, 0)]
    [OtUInt32Array("ConditionOffset", 2, CountPropertyName = "ConditionCount")]
    [OtSubTableOffsetArray("Condition", "ConditionOffset", typeof(Condition))]
    public readonly partial struct ConditionSet
    {
    }

    [OtSubTable(2)]
    [OtField("ConditionFormat", OtFieldKind.UInt16, 0)]
    [OtDiscriminant(nameof(ConditionFormat))]
    [OtCase(1, typeof(Condition.ConditionFormat1), Name = "Format1")]
    public readonly partial struct Condition
    {
        [OtSubTable(8)]
        [OtField("AxisIndex", OtFieldKind.UInt16, 2)]
        [OtField("FilterRangeMinValueRaw", OtFieldKind.Int16, 4)]
        [OtField("FilterRangeMaxValueRaw", OtFieldKind.Int16, 6)]
        public readonly partial struct ConditionFormat1
        {
            public F2Dot14 FilterRangeMinValue => new(FilterRangeMinValueRaw);
            public F2Dot14 FilterRangeMaxValue => new(FilterRangeMaxValueRaw);
        }
    }

    [OtSubTable(4)]
    [OtField("Version", OtFieldKind.UInt16, 0)]
    [OtField("SubstitutionCount", OtFieldKind.UInt16, 2)]
    [OtSequentialRecordArray("SubstitutionRecord", 4, 6, CountPropertyName = "SubstitutionCount")]
    public readonly partial struct FeatureTableSubstitution
    {
        public readonly struct SubstitutionRecord
        {
            public ushort FeatureIndex { get; }
            public uint FeatureOffset { get; }

            public SubstitutionRecord(ushort featureIndex, uint featureOffset)
            {
                FeatureIndex = featureIndex;
                FeatureOffset = featureOffset;
            }
        }

        public bool TryGetFeature(SubstitutionRecord record, out OtlLayoutTable.Feature feature)
        {
            feature = default;

            uint rel = record.FeatureOffset;
            if (rel == 0 || rel > int.MaxValue)
                return false;

            int abs = checked(_offset + (int)rel);
            return OtlLayoutTable.Feature.TryCreate(_table, abs, out feature);
        }
    }
}
