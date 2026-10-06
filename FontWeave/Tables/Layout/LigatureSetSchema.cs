using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LigatureSetLayout
{
    internal const int HeaderLength = 2, CountAt = 0, OffsetStride = 2;
}

// All ligatures beginning with one covered glyph (GSUB lookup type 4, second level).
// The Ligature offset order is preference order, so no ordering relation is declared.
// The Ligature reference carries the set's own RecordsEnd floor (2 + 2*ligatureCount).
[BinaryTable(GsubLayout.Tag, LigatureSetLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("LigatureCount", WireType.UInt16, LigatureSetLayout.CountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("LigatureOffsetRecord", "LigatureCount", LigatureSetLayout.HeaderLength,
    LigatureSetLayout.OffsetStride)]
[BinaryElement("LigatureOffsetRecord", "LigatureOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 2, Count = "LigatureCount", Stride = LigatureSetLayout.OffsetStride)]
[BinaryReference("Ligature", "LigatureOffset", typeof(LigatureView), Array = "LigatureOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct LigatureSetView { }
