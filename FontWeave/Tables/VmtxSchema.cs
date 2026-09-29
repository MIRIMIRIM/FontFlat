using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class VmtxLayout
{
    internal const uint Tag = 0x766D7478;
    internal const int LongStride = 4, TailStride = 2;
}

// Record positions/counts are bound by the partial from external G/N, never read from wire.
[BinaryTable(VmtxLayout.Tag, 1)]
[BinaryRecord("LongMetric", VmtxLayout.LongStride)]
[BinaryElement("LongMetric", "AdvanceHeight", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("LongMetric", "TopSideBearing", WireType.Int16, 2, DataField.Payload)]
[BinaryRecord("TailBearing", VmtxLayout.TailStride)]
[BinaryElement("TailBearing", "TopSideBearing", WireType.Int16, 0, DataField.Payload)]
public readonly ref partial struct VmtxView { }
