using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class BaseTagListLayout
{
    internal const int HeaderLength = 2, TagStride = 4;
}

// The baseline-tag list: a counted array of raw UInt32 tags with stride 4 and no
// offset, no reference and no version field - a different layout from the Layout
// TagList (whose TagRecord carries an offset, T4-07 section 0), so its own view is
// the correct shape (the AGENTS one-layout-one-view rule). Alphabetical order is an
// observation (OQ-2: the spec's must/should wording conflict stays off the hard
// path); the original wire order is what the semantic layer records.
[BinaryTable(BaseLayout.Tag, BaseTagListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("BaseTagCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaselineTag", "BaseTagCount", BaseTagListLayout.HeaderLength, BaseTagListLayout.TagStride)]
[BinaryElement("BaselineTag", "Tag", WireType.UInt32, 0, DataField.Tag)]
public readonly ref partial struct BaseTagListView { }
