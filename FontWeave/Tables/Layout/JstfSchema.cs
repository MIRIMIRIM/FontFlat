using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfLayout
{
    internal const uint Tag = 0x4A535446;
    // The packed u32 read is the GSUB/GPOS header family shape (GdefSchema/BaseSchema
    // precedent); major/minor are the two u16 halves of one supported version.
    internal const uint Version10 = 0x00010000;
    internal const int Header10 = 6,
        ScriptCountAt = 4, ScriptRecordsAt = 6, ScriptRecordSize = 6,
        LangSysRecordSize = 6, PrioritySize = 20;
}

// The JSTF root (T4-09 section 2): the packed 1.0-only version word, the counted
// JstfScriptRecord array (Tag + Offset16, stride 6, alphabetical order is the OQ-2
// observation) and the per-item Required JstfScript references. Every offset in the
// table is relative to its own referencing structure, so each reference origin stays
// the view start; the RecordsEnd floor makes every edge strictly forward, which is
// the declaration-level DAG guarantee (an offset pointing back at the header refuses
// natively as StorageBeforeRecords, T4-07's scriptOffset shape).
[BinaryTable(JstfLayout.Tag, JstfLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { JstfLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ScriptCount", WireType.UInt16, JstfLayout.ScriptCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("ScriptRecord", "ScriptCount", JstfLayout.ScriptRecordsAt, JstfLayout.ScriptRecordSize)]
[BinaryElement("ScriptRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("ScriptRecord", "Offset", WireType.UInt16, 4, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = JstfLayout.ScriptRecordsAt,
    Count = "ScriptCount", Stride = JstfLayout.ScriptRecordSize)]
[BinaryReference("Script", "Offset", typeof(JstfScriptView), Array = "ScriptRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct JstfView { }
