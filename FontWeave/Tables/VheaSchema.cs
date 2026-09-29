using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class VheaLayout
{
    internal const uint Tag = 0x76686561;
    internal const int Length = 36;
    internal const uint Version10 = 0x00010000, Version11 = 0x00011000;
}

[BinaryTable(VheaLayout.Tag, VheaLayout.Length, VersionField = "VersionRaw", SupportedVersions = new uint[] { VheaLayout.Version10, VheaLayout.Version11 })]
[BinaryScalar("VersionRaw", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("Ascender", WireType.Int16, 4, DataField.Payload)]
[BinaryScalar("Descender", WireType.Int16, 6, DataField.Payload)]
[BinaryScalar("LineGap", WireType.Int16, 8, DataField.Payload)]
[BinaryScalar("AdvanceHeightMax", WireType.UInt16, 10, DataField.Payload)]
[BinaryScalar("MinTopSideBearing", WireType.Int16, 12, DataField.Payload)]
[BinaryScalar("MinBottomSideBearing", WireType.Int16, 14, DataField.Payload)]
[BinaryScalar("YMaxExtent", WireType.Int16, 16, DataField.Payload)]
[BinaryScalar("CaretSlopeRise", WireType.Int16, 18, DataField.Payload)]
[BinaryScalar("CaretSlopeRun", WireType.Int16, 20, DataField.Payload)]
[BinaryScalar("CaretOffset", WireType.Int16, 22, DataField.Payload)]
[BinaryScalar("Reserved1", WireType.Int16, 24, DataField.Payload)]
[BinaryScalar("Reserved2", WireType.Int16, 26, DataField.Payload)]
[BinaryScalar("Reserved3", WireType.Int16, 28, DataField.Payload)]
[BinaryScalar("Reserved4", WireType.Int16, 30, DataField.Payload)]
[BinaryScalar("MetricDataFormat", WireType.Int16, 32, DataField.Payload)]
[BinaryScalar("NumOfLongVerMetrics", WireType.UInt16, 34, DataField.Count, Semantic = NumberSemantic.Count)]
public readonly ref partial struct VheaView { }
