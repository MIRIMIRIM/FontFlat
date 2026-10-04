using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class StaticDeviceLayout
{
    internal const int HeaderLength = 6, StartAt = 0, EndAt = 2, FormatAt = 4, WordStride = 2;

    internal static int WordCount(int count, int bits) => (count * bits + 15) / 16;

    // Compile-side model admission only (TryCreate value domains without spans, card
    // OQ-6); bound views run on the generated contract-version-1 chain above.
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

// 0x8000 is a known version whose structures stay conditionally inactive, so every
// entry point reports the same UnsupportedOperation; other unknown formats stay
// UnsupportedVersion at the version gate (card 1.3-b, main-agent adjudication).
[BinaryTable(0, StaticDeviceLayout.HeaderLength, ContractVersion = 1,
    VersionField = "DeltaFormat", SupportedVersions = new uint[] { 1, 2, 3, 0x8000 })]
[BinaryScalar("StartSize", WireType.UInt16, StaticDeviceLayout.StartAt, DataField.Count)]
[BinaryScalar("EndSize", WireType.UInt16, StaticDeviceLayout.EndAt, DataField.Count)]
[BinaryScalar("DeltaFormat", WireType.UInt16, StaticDeviceLayout.FormatAt, DataField.Version)]
[BinaryArray("Word", "PackedWordCount", StaticDeviceLayout.HeaderLength, StaticDeviceLayout.WordStride,
    WhenVersions = new uint[] { 1, 2, 3 })]
[BinaryElement("Word", "Value", WireType.UInt16, 0, DataField.Payload)]
[BinaryComputed("DeltaValueCount", "DeltaValueCountOf",
    Inputs = new[] { "StartSize", "EndSize" }, Maximum = 65536ul,
    Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count,
    DiagnosticSource = "EndSize", WhenVersions = new uint[] { 1, 2, 3 })]
[BinaryComputed("PackedWordCount", "PackedWordCountOf",
    Inputs = new[] { "DeltaValueCount", "DeltaFormat" }, Maximum = 32768ul,
    Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count,
    DiagnosticSource = "EndSize", WhenVersions = new uint[] { 1, 2, 3 })]
public readonly ref partial struct StaticDeviceView { }
