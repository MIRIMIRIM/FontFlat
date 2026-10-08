using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseScriptLayout
{
    internal const int HeaderLength = 6, BaseValuesOffsetField = 0,
        DefaultMinMaxOffsetField = 2, BaseLangSysCountAt = 4,
        RecordStride = 6, RecordOffsetField = 4;
}

// One BaseScript: two Optional/ZeroAllowed slots (the spec's two "may be NULL" notes -
// a NULL BaseValues is expressible, zero real witnesses, R6) followed by the counted
// BaseLangSysRecord array whose per-item MinMax reference is Required (no NULL note).
// The three references' floor is the dynamic RecordsEnd derived - the GdefSchema
// MarkSets/MarkBasePos head-tail derived precedent (T4-07 section 2.1).
[BinaryTable(BaseLayout.Tag, BaseScriptLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("BaseValuesOffset", WireType.UInt16, BaseScriptLayout.BaseValuesOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("DefaultMinMaxOffset", WireType.UInt16, BaseScriptLayout.DefaultMinMaxOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("BaseLangSysCount", WireType.UInt16, BaseScriptLayout.BaseLangSysCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaseLangSysRecord", "BaseLangSysCount", BaseScriptLayout.HeaderLength,
    BaseScriptLayout.RecordStride)]
[BinaryElement("BaseLangSysRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("BaseLangSysRecord", "MinMaxOffset", WireType.UInt16, BaseScriptLayout.RecordOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "BaseLangSysCount", Stride = BaseScriptLayout.RecordStride)]
[BinaryReference("BaseValues", "BaseValuesOffset", typeof(BaseValuesView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("DefaultMinMax", "DefaultMinMaxOffset", typeof(BaseMinMaxView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("MinMax", "MinMaxOffset", typeof(BaseMinMaxView), Array = "BaseLangSysRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct BaseScriptView { }
