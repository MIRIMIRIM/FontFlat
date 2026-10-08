using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathConstantsLayout
{
    internal const int ScriptPercentScaleDownAt = 0, ScriptScriptPercentScaleDownAt = 2,
        DelimitedSubFormulaMinHeightAt = 4, DisplayOperatorMinHeightAt = 6,
        RadicalDegreeBottomRaisePercentAt = 212;
}

// MathConstants (T4-10 section 2, OQ-2's lean ruling): the version-less 56-field fixed
// body (the AttachList family shape) whose three int16 percentages, two UFWORD minimum
// heights and 51 MathValueRecords sit at frozen offsets - the table declares no count
// field, so the fixed layout is the declaration's essence and no pseudo-count is
// invented. The 51 records are the 102 explicit scalars of the ruling: each record is
// one FWORD value plus one Optional/ZeroAllowed deviceOffset (the spec's "May be
// NULL"), and the device targets resolve through the existing StaticDeviceView opener
// at the semantic layer (the JstfMax OQ-6 handwritten-opener shape; the JSTF mod list
// precedent for a wire-identical family staying one declaration). The BodyEnd constant
// keeps every device edge strictly forward of the fixed body (BaseCoord fmt3's floor
// shape); the record order is the semantic constant order and is never re-sorted.
[BinaryTable(MathLayout.Tag, MathLayout.ConstantsSize, ContractVersion = 1)]
[BinaryScalar("ScriptPercentScaleDown", WireType.Int16, MathConstantsLayout.ScriptPercentScaleDownAt, DataField.Payload)]
[BinaryScalar("ScriptScriptPercentScaleDown", WireType.Int16, MathConstantsLayout.ScriptScriptPercentScaleDownAt, DataField.Payload)]
[BinaryScalar("DelimitedSubFormulaMinHeight", WireType.UInt16, MathConstantsLayout.DelimitedSubFormulaMinHeightAt, DataField.Payload)]
[BinaryScalar("DisplayOperatorMinHeight", WireType.UInt16, MathConstantsLayout.DisplayOperatorMinHeightAt, DataField.Payload)]
[BinaryScalar("MathLeadingValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 0 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("MathLeadingDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 0 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("AxisHeightValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 1 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("AxisHeightDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 1 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("AccentBaseHeightValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 2 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("AccentBaseHeightDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 2 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FlattenedAccentBaseHeightValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 3 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FlattenedAccentBaseHeightDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 3 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SubscriptShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 4 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SubscriptShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 4 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SubscriptTopMaxValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 5 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SubscriptTopMaxDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 5 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SubscriptBaselineDropMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 6 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SubscriptBaselineDropMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 6 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SuperscriptShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 7 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SuperscriptShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 7 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SuperscriptShiftUpCrampedValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 8 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SuperscriptShiftUpCrampedDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 8 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SuperscriptBottomMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 9 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SuperscriptBottomMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 9 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SuperscriptBaselineDropMaxValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 10 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SuperscriptBaselineDropMaxDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 10 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SubSuperscriptGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 11 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SubSuperscriptGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 11 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SuperscriptBottomMaxWithSubscriptValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 12 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SuperscriptBottomMaxWithSubscriptDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 12 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SpaceAfterScriptValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 13 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SpaceAfterScriptDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 13 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("UpperLimitGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 14 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("UpperLimitGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 14 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("UpperLimitBaselineRiseMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 15 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("UpperLimitBaselineRiseMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 15 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("LowerLimitGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 16 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("LowerLimitGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 16 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("LowerLimitBaselineDropMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 17 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("LowerLimitBaselineDropMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 17 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackTopShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 18 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackTopShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 18 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackTopDisplayStyleShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 19 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackTopDisplayStyleShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 19 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackBottomShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 20 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackBottomShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 20 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackBottomDisplayStyleShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 21 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackBottomDisplayStyleShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 21 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 22 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 22 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StackDisplayStyleGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 23 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StackDisplayStyleGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 23 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StretchStackTopShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 24 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StretchStackTopShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 24 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StretchStackBottomShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 25 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StretchStackBottomShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 25 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StretchStackGapAboveMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 26 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StretchStackGapAboveMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 26 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("StretchStackGapBelowMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 27 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("StretchStackGapBelowMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 27 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionNumeratorShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 28 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionNumeratorShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 28 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionNumeratorDisplayStyleShiftUpValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 29 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionNumeratorDisplayStyleShiftUpDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 29 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionDenominatorShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 30 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionDenominatorShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 30 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionDenominatorDisplayStyleShiftDownValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 31 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionDenominatorDisplayStyleShiftDownDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 31 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionNumeratorGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 32 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionNumeratorGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 32 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionNumDisplayStyleGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 33 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionNumDisplayStyleGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 33 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionRuleThicknessValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 34 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionRuleThicknessDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 34 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionDenominatorGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 35 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionDenominatorGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 35 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("FractionDenomDisplayStyleGapMinValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 36 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("FractionDenomDisplayStyleGapMinDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 36 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SkewedFractionHorizontalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 37 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SkewedFractionHorizontalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 37 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("SkewedFractionVerticalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 38 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("SkewedFractionVerticalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 38 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("OverbarVerticalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 39 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("OverbarVerticalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 39 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("OverbarRuleThicknessValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 40 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("OverbarRuleThicknessDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 40 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("OverbarExtraAscenderValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 41 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("OverbarExtraAscenderDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 41 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("UnderbarVerticalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 42 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("UnderbarVerticalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 42 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("UnderbarRuleThicknessValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 43 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("UnderbarRuleThicknessDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 43 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("UnderbarExtraDescenderValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 44 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("UnderbarExtraDescenderDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 44 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalVerticalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 45 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalVerticalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 45 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalDisplayStyleVerticalGapValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 46 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalDisplayStyleVerticalGapDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 46 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalRuleThicknessValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 47 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalRuleThicknessDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 47 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalExtraAscenderValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 48 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalExtraAscenderDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 48 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalKernBeforeDegreeValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 49 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalKernBeforeDegreeDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 49 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalKernAfterDegreeValue", WireType.Int16, MathLayout.ConstantsValueRecordsAt + 50 * MathLayout.ValueRecordSize + MathLayout.ValueValueAt, DataField.Payload)]
[BinaryScalar("RadicalKernAfterDegreeDeviceOffset", WireType.UInt16, MathLayout.ConstantsValueRecordsAt + 50 * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("RadicalDegreeBottomRaisePercent", WireType.Int16, MathConstantsLayout.RadicalDegreeBottomRaisePercentAt, DataField.Payload)]
[BinaryDerived("BodyEnd", Constant = MathLayout.ConstantsSize)]
public readonly ref partial struct MathConstantsView { }
