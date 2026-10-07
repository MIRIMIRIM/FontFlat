using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class StaticDeviceLayout
{
    internal const int HeaderLength = 6, StartAt = 0, EndAt = 2, FormatAt = 4, WordStride = 2;

    internal static int WordCount(int count, int bits) => (count * bits + 15) / 16;

    // Compile-side model admission only (TryCreate value domains without spans, card
    // OQ-6); bound views run on the generated contract-version-1 chain above.
    // T4-06 section 3.4: the 0x8000 VariationIndex form activates - start/end carry
    // the outer/inner delta-set indices (indices, so the ppem end<start ordering
    // constraint does not apply), the delta array is empty and the body is the
    // 6-byte header; the store-domain validation lives at the consumption chain
    // (the GDEF store is a cross-table input), not at this wire-level header.
    internal static ReadStatus CheckHeader(ushort start, ushort end, ushort format, DataLocation location,
        out int count, out int bits, out int consumed, out FontDiagnostic diagnostic)
    {
        count = bits = consumed = 0;
        if (format == 0x8000)
        {
            count = bits = 0;
            consumed = HeaderLength;
            diagnostic = default;
            return ReadStatus.Done;
        }
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

// The version gate keeps 0x8000 in the known set; formats 1..3's words stay
// conditionally inactive outside their version (the branched arrays empty out) and
// the 0x8000 VariationDevice shape is the semantic partial's (StartSize/EndSize
// reinterpreted as the outer/inner delta-set indices, no packed words, T4-06 3.4).
[BinaryTable(0, StaticDeviceLayout.HeaderLength, ContractVersion = 1, GenerateStructureScan = true,
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
