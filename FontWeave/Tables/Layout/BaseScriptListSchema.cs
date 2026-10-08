using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseScriptListLayout
{
    internal const int HeaderLength = 2, RecordStride = 6, RecordOffsetField = 4;
}

// The counted BaseScriptRecord array (LayoutTagList's record shape with BASE's offset
// discipline): each row is a script tag plus a Required per-item BaseScript reference
// (no spec NULL note - the OQ-1 face; the LayoutTagList Script-branch precedent).
// Alphabetical tag order is an observation (OQ-2), not a refusal.
[BinaryTable(BaseLayout.Tag, BaseScriptListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("BaseScriptCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaseScriptRecord", "BaseScriptCount", BaseScriptListLayout.HeaderLength,
    BaseScriptListLayout.RecordStride)]
[BinaryElement("BaseScriptRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("BaseScriptRecord", "Offset", WireType.UInt16, BaseScriptListLayout.RecordOffsetField,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 2, Count = "BaseScriptCount", Stride = BaseScriptListLayout.RecordStride)]
[BinaryReference("BaseScript", "Offset", typeof(BaseScriptView), Array = "BaseScriptRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "RecordsEnd")]
public readonly ref partial struct BaseScriptListView { }
