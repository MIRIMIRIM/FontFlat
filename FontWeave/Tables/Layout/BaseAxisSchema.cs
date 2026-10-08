using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseAxisLayout
{
    internal const int HeaderLength = 4;
}

// One BASE direction (horiz/vert share this single layout, T4-07 section 2). The two
// slots differ in null discipline exactly as the spec annotates them: the BaseTagList
// is "may be NULL" (Optional), while the BaseScriptList carries no NULL note - the
// OQ-1 ruling keeps it Required (zero offset = native refusal, a synthetic pin), the
// one asymmetry against the tag-list slot. Axis direction semantics (Y vs X value
// interpretation) are comment-level observations with no wire difference.
[BinaryTable(BaseLayout.Tag, BaseAxisLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("BaseTagListOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("BaseScriptListOffset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("AxisEnd", Constant = BaseAxisLayout.HeaderLength)]
[BinaryReference("BaseTagList", "BaseTagListOffset", typeof(BaseTagListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "AxisEnd")]
[BinaryReference("BaseScriptList", "BaseScriptListOffset", typeof(BaseScriptListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "AxisEnd")]
public readonly ref partial struct BaseAxisView { }
