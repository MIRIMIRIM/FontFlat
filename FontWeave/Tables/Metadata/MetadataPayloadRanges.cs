using FontWeave.Core;

namespace FontWeave.Tables;

internal readonly record struct MetadataPayloadRange(uint Offset, uint Length, int Ordinal);

internal static class MetadataPayloadRanges
{
    // Callers first admit the array and bounded sort work, and check each range locally.
    internal static ReadStatus Validate(MetadataPayloadRange[] ranges, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        Array.Sort(ranges, static (left, right) => left.Offset.CompareTo(right.Offset));
        ulong end = 0;
        foreach (var range in ranges)
        {
            // Empty metadata payloads occupy no bytes, even inside a nonempty range.
            if (range.Length == 0) continue;
            if (range.Offset < end)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                    location.At(DataField.Payload, range.Offset, range.Ordinal), out diagnostic,
                    range.Offset, end);
            end = (ulong)range.Offset + range.Length;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
