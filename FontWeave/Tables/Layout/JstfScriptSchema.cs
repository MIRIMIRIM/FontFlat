using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfScriptLayout
{
    internal const int HeaderLength = 6,
        ExtenderGlyphOffsetAt = 0, DefJstfLangSysOffsetAt = 2, LangSysCountAt = 4,
        LangSysRecordsAt = 6, LangSysRecordSize = 6, RecordTagField = 0, RecordOffsetField = 4;
}

// One JstfScript (T4-09 section 2): both Offset16 slots are the spec's "may be NULL"
// (Optional/ZeroAllowed, the BaseScript dual-slot shape) and jstfLangSysCount may be
// zero (OQ-5's legal empty script: both NULL offsets plus a zero count). The two
// Optional references and the per-item Required langsys references all share the
// RecordsEnd floor, so every edge is strictly forward.
[BinaryTable(JstfLayout.Tag, JstfScriptLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ExtenderGlyphOffset", WireType.UInt16, JstfScriptLayout.ExtenderGlyphOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("DefJstfLangSysOffset", WireType.UInt16, JstfScriptLayout.DefJstfLangSysOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("LangSysCount", WireType.UInt16, JstfScriptLayout.LangSysCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LangSysRecord", "LangSysCount", JstfScriptLayout.LangSysRecordsAt,
    JstfScriptLayout.LangSysRecordSize)]
[BinaryElement("LangSysRecord", "Tag", WireType.UInt32, JstfScriptLayout.RecordTagField, DataField.Tag)]
[BinaryElement("LangSysRecord", "Offset", WireType.UInt16, JstfScriptLayout.RecordOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = JstfScriptLayout.LangSysRecordsAt,
    Count = "LangSysCount", Stride = JstfScriptLayout.LangSysRecordSize)]
[BinaryReference("ExtenderGlyph", "ExtenderGlyphOffset", typeof(JstfExtenderGlyphView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("DefJstfLangSys", "DefJstfLangSysOffset", typeof(JstfLangSysView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "RecordsEnd")]
[BinaryReference("LangSys", "Offset", typeof(JstfLangSysView), Array = "LangSysRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct JstfScriptView { }
