using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class HdmxLayout
{
    internal const uint Tag = 0x68646D78;
    internal const int HeaderLength = 8, RecordHeaderLength = 2;
    internal static int CanonicalStride(int numGlyphs) => (numGlyphs + RecordHeaderLength + 3) & ~3;
}

[BinaryTable(HdmxLayout.Tag, HdmxLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumRecords", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("SizeDeviceRecord", WireType.UInt32, 4, DataField.Length, Semantic = NumberSemantic.Count)]
// Record positions depend on the wire stride; widths also depend on external maxp.numGlyphs.
[BinaryRecord("DeviceRecordHeader", HdmxLayout.RecordHeaderLength)]
[BinaryElement("DeviceRecordHeader", "PixelSize", WireType.UInt8, 0, DataField.Payload)]
[BinaryElement("DeviceRecordHeader", "MaxWidth", WireType.UInt8, 1, DataField.Payload)]
[BinaryRecord("DeviceByte", 1)]
[BinaryElement("DeviceByte", "Value", WireType.UInt8, 0, DataField.Payload)]
public readonly ref partial struct HdmxView { }
