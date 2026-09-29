using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class HheaLayout
{
    internal const uint Tag = 0x68686561;
    internal const int Length = 36;
}

[BinaryTable(HheaLayout.Tag, HheaLayout.Length, VersionField = "MajorVersion", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Version)]
[BinaryScalar("Ascender", WireType.Int16, 4, DataField.Payload)]
[BinaryScalar("Descender", WireType.Int16, 6, DataField.Payload)]
[BinaryScalar("LineGap", WireType.Int16, 8, DataField.Payload)]
[BinaryScalar("AdvanceWidthMax", WireType.UInt16, 10, DataField.Payload)]
[BinaryScalar("MinLeftSideBearing", WireType.Int16, 12, DataField.Payload)]
[BinaryScalar("MinRightSideBearing", WireType.Int16, 14, DataField.Payload)]
[BinaryScalar("XMaxExtent", WireType.Int16, 16, DataField.Payload)]
[BinaryScalar("CaretSlopeRise", WireType.Int16, 18, DataField.Payload)]
[BinaryScalar("CaretSlopeRun", WireType.Int16, 20, DataField.Payload)]
[BinaryScalar("CaretOffset", WireType.Int16, 22, DataField.Payload)]
[BinaryScalar("Reserved1", WireType.Int16, 24, DataField.Payload)]
[BinaryScalar("Reserved2", WireType.Int16, 26, DataField.Payload)]
[BinaryScalar("Reserved3", WireType.Int16, 28, DataField.Payload)]
[BinaryScalar("Reserved4", WireType.Int16, 30, DataField.Payload)]
[BinaryScalar("MetricDataFormat", WireType.Int16, 32, DataField.Payload)]
[BinaryScalar("NumberOfHMetrics", WireType.UInt16, 34, DataField.Count, Semantic = NumberSemantic.Count)]
public readonly ref partial struct HheaView { }
