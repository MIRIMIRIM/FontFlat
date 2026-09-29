using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class HmtxLayout
{
    internal const uint Tag = 0x686D7478;
    internal const int LongStride = 4, TailStride = 2;
}

// Record positions/counts are bound by the partial from external G/N, never read from wire.
[BinaryTable(HmtxLayout.Tag, 1)]
[BinaryRecord("LongMetric", HmtxLayout.LongStride)]
[BinaryElement("LongMetric", "AdvanceWidth", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("LongMetric", "LeftSideBearing", WireType.Int16, 2, DataField.Payload)]
[BinaryRecord("TailBearing", HmtxLayout.TailStride)]
[BinaryElement("TailBearing", "LeftSideBearing", WireType.Int16, 0, DataField.Payload)]
public readonly ref partial struct HmtxView { }
