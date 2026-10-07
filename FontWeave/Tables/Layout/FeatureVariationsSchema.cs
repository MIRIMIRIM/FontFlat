using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class FeatureVariationsLayout
{
    // FeatureVariations lives inside GSUB (the GPOS root wires its own in T4-06), so
    // the family tag names the GSUB constant for the same reason the GDEF children
    // name GdefLayout.Tag.
    internal const int HeaderLength = 8, RecordCountAt = 4, RecordsAt = 8, RecordStride = 8;
}

internal static class ConditionSetLayout
{
    internal const int HeaderLength = 2, OffsetsAt = 2, OffsetStride = 4;
}

internal static class ConditionLayout
{
    internal const int HeaderLength = 8, AxisIndexAt = 2, MinValueAt = 4, MaxValueAt = 6;
}

internal static class FeatureTableSubstLayout
{
    internal const int HeaderLength = 6, SubstitutionCountAt = 4, RecordsAt = 6, RecordStride = 6,
        FeatureIndexAt = 0, AlternateOffsetAt = 2;
}

// FeatureVariations 1.0 (GSUB 1.1's variations block, the frozen chapter2 section):
// the record count is the family's UInt32 word and the record stride is 8 (two
// Offset32 slots). Both offsets carry spec-defined NULL semantics - a NULL
// conditionSet matches universally and a NULL featureTableSubstitution is a no-op -
// so both references open as present=false through the Optional null policy (the
// ItemVariationStore dataOffsets form, OQ-T4-04-6's declarative reading). Every
// record offset is relative to the FeatureVariations start (all ViewStart).
[BinaryTable(GsubLayout.Tag, FeatureVariationsLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { GsubLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("FeatureVariationRecordCount", WireType.UInt32, FeatureVariationsLayout.RecordCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("FeatureVariationRecord", "FeatureVariationRecordCount", FeatureVariationsLayout.RecordsAt,
    FeatureVariationsLayout.RecordStride)]
[BinaryElement("FeatureVariationRecord", "ConditionSetOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryElement("FeatureVariationRecord", "FeatureTableSubstOffset", WireType.UInt32, 4, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("RecordsEnd", Constant = FeatureVariationsLayout.RecordsAt,
    Count = "FeatureVariationRecordCount", Stride = FeatureVariationsLayout.RecordStride)]
[BinaryReference("ConditionSet", "ConditionSetOffset", typeof(ConditionSetView),
    Array = "FeatureVariationRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "RecordsEnd")]
[BinaryReference("FeatureTableSubst", "FeatureTableSubstOffset", typeof(FeatureTableSubstView),
    Array = "FeatureVariationRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "RecordsEnd")]
public readonly ref partial struct FeatureVariationsView { }

// One ConditionSet: a counted Offset32 array of conditions. Spec defines no NULL
// condition, so the reference policy is Required; an empty set is legal and matches
// universally (the semantic layer's universal-condition reading).
[BinaryTable(GsubLayout.Tag, ConditionSetLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ConditionCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("ConditionOffsetRecord", "ConditionCount", ConditionSetLayout.OffsetsAt,
    ConditionSetLayout.OffsetStride)]
[BinaryElement("ConditionOffsetRecord", "ConditionOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = ConditionSetLayout.OffsetsAt,
    Count = "ConditionCount", Stride = ConditionSetLayout.OffsetStride)]
[BinaryReference("Condition", "ConditionOffset", typeof(ConditionView),
    Array = "ConditionOffsetRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct ConditionSetView { }

// One Condition format 1 (the only format the frozen corpus carries, R5): the raw
// F2DOT14 pair stays Int16 raw values (T5-02's raw Fixed philosophy - no normalized
// conversion at this level). The format word is deliberately NOT a version gate:
// unknown condition formats are the spec's tolerance domain ("fail to match the
// condition set, but continue"), so the view opens for any format and the semantic
// layer owns the layering - typed validation refuses unknown formats while the
// condition evaluator reports them as unmatched observations (OQ-T4-04-7).
[BinaryTable(GsubLayout.Tag, ConditionLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("AxisIndex", WireType.UInt16, ConditionLayout.AxisIndexAt, DataField.Payload)]
[BinaryScalar("FilterRangeMinValue", WireType.Int16, ConditionLayout.MinValueAt, DataField.Payload)]
[BinaryScalar("FilterRangeMaxValue", WireType.Int16, ConditionLayout.MaxValueAt, DataField.Payload)]
public readonly ref partial struct ConditionView { }

// One FeatureTableSubst: the counted 6-byte record array whose Offset32 alternate
// targets reuse the LayoutFeatureView (a typed target from outside the GSUB leaf
// family, per the card's reference table). The records' strictly increasing
// featureIndex ordering is the semantic layer's hard check (the STAT format-4
// "must be different" precedent, OQ-T4-04-8).
[BinaryTable(GsubLayout.Tag, FeatureTableSubstLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { GsubLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("SubstitutionCount", WireType.UInt16, FeatureTableSubstLayout.SubstitutionCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("SubstitutionRecord", "SubstitutionCount", FeatureTableSubstLayout.RecordsAt,
    FeatureTableSubstLayout.RecordStride)]
[BinaryElement("SubstitutionRecord", "FeatureIndex", WireType.UInt16,
    FeatureTableSubstLayout.FeatureIndexAt, DataField.Payload)]
[BinaryElement("SubstitutionRecord", "AlternateFeatureOffset", WireType.UInt32,
    FeatureTableSubstLayout.AlternateOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = FeatureTableSubstLayout.RecordsAt,
    Count = "SubstitutionCount", Stride = FeatureTableSubstLayout.RecordStride)]
[BinaryReference("AlternateFeature", "AlternateFeatureOffset", typeof(LayoutFeatureView),
    Array = "SubstitutionRecord", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct FeatureTableSubstView { }
