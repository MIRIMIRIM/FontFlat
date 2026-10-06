using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class AttachPointLayout
{
    internal const int HeaderLength = 2, CountAt = 0, PointStride = 2;
}

// Contour-point indices attached to one covered glyph. The values are outline-space
// indices carried opaquely (they resolve only against the owning glyph's outline,
// a cross-table domain this single-table view does not see, OQ-T4-02-2 sibling).
[BinaryTable(GdefLayout.Tag, AttachPointLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("PointCount", WireType.UInt16, AttachPointLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("PointIndex", "PointCount", AttachPointLayout.HeaderLength, AttachPointLayout.PointStride)]
[BinaryElement("PointIndex", "Value", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct AttachPointView { }
