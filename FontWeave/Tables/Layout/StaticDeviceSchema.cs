using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class StaticDeviceLayout
{
    internal const int HeaderLength = 6, StartAt = 0, EndAt = 2, FormatAt = 4, WordStride = 2;

    internal static int WordCount(int count, int bits) => (count * bits + 15) / 16;

    internal static ReadStatus CheckHeader(ushort start, ushort end, ushort format, DataLocation location,
        out int count, out int bits, out int consumed, out FontDiagnostic diagnostic)
    {
        count = bits = consumed = 0;
        if (format == 0x8000)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Version, FormatAt), out diagnostic, format);
        if (format is not (1 or 2 or 3))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, FormatAt), out diagnostic, format);
        if (end < start)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, EndAt), out diagnostic, end, start);
        count = (int)end - start + 1; // Inclusive UInt16 bounds can declare 65536 values.
        bits = 1 << format;
        consumed = HeaderLength + WordStride * WordCount(count, bits);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

[BinaryTable(0, StaticDeviceLayout.HeaderLength, VersionField = "DeltaFormat", SupportedVersions = new uint[] { 1, 2, 3 })]
[BinaryScalar("StartSize", WireType.UInt16, StaticDeviceLayout.StartAt, DataField.Count)]
[BinaryScalar("EndSize", WireType.UInt16, StaticDeviceLayout.EndAt, DataField.Count)]
[BinaryScalar("DeltaFormat", WireType.UInt16, StaticDeviceLayout.FormatAt, DataField.Version)]
[BinaryRecord("PackedWord", StaticDeviceLayout.WordStride)]
[BinaryElement("PackedWord", "Value", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct StaticDeviceView { }
