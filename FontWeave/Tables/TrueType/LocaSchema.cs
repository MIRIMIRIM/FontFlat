using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class LocaLayout
{
    internal const uint Tag = 0x6C6F6361;
    internal const int ShortStride = 2, LongStride = 4;
    internal const uint ShortScale = 2;
    internal const int MinimumHeader = 2;
}

// loca has no fixed header: it is one array of format-selected offsets whose
// element count and selector come from the same source face through LocaContext,
// never from wire. The two record shapes are declared here so the generated reader
// can bind each element; the partial selects the shape from the context.
[BinaryTable(LocaLayout.Tag, LocaLayout.MinimumHeader)]
[BinaryRecord("ShortOffset", LocaLayout.ShortStride)]
[BinaryElement("ShortOffset", "Value", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryRecord("LongOffset", LocaLayout.LongStride)]
[BinaryElement("LongOffset", "Value", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct LocaView { }
