using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Handwritten validation and Unicode semantics over the generated name layout.</summary>
public readonly ref partial struct NameView
{
    /// <summary>
    /// Checks the finite name profile, not BCP-47 syntax or whole-font consistency.
    /// Repeated payload references count separately toward the scan budget.
    /// </summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic,
        int maxReferencedPayloadBytes = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferencedPayloadBytes);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;

        int count = DeclaredCount;
        // A valid metadata boundary is required before interpreting any record payload.
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            status = TryGetRecord(ordinal, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = TryGetLanguageTagCount(out ushort tagCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int ordinal = 0; ordinal < tagCount; ordinal++)
        {
            status = TryGetLanguageTag(ordinal, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = TryGetMetadataEnd(out ulong metadataEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (StorageOffset < metadataEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.At(DataField.StorageOffset, 4), out diagnostic,
                StorageOffset, metadataEnd);
        if (StorageOffset > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.StorageOffset, 4), out diagnostic,
                StorageOffset, (ulong)_data.Length);

        ulong referencedPayloadBytes = 0;
        RecordData previous = default;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong rowOffset = 6UL + (ulong)ordinal * 12;
            if (ordinal > 0 && CompareKeys(previous, record) > 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.None, rowOffset, ordinal), out diagnostic);
            if (Format == 1 && record.LanguageId >= 0x8000 &&
                record.LanguageId - 0x8000 >= tagCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.LanguageId, rowOffset + 4, ordinal),
                    out diagnostic, record.LanguageId - 0x8000u, tagCount);
            if (Format == 0 && record.LanguageId >= 0x8000 &&
                record.PlatformId is < 240 or > 255)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.LanguageId, rowOffset + 4, ordinal),
                    out diagnostic, record.LanguageId);

            referencedPayloadBytes += record.Length;
            ulong payloadOffset = (ulong)StorageOffset + record.StorageRelativeOffset;
            if (referencedPayloadBytes > (ulong)maxReferencedPayloadBytes)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.At(DataField.Payload, payloadOffset, ordinal), out diagnostic,
                    referencedPayloadBytes, (ulong)maxReferencedPayloadBytes);
            status = TryGetRecordPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (IsUtf16(record.PlatformId, record.EncodingId) &&
                !IsValidUtf16BigEndian(payload))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    Location.At(DataField.Payload, payloadOffset, ordinal), out diagnostic,
                    record.Length);
            previous = record;
        }

        for (int ordinal = 0; ordinal < tagCount; ordinal++)
        {
            status = TryGetLanguageTag(ordinal, out LanguageTagData tag, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong rowOffset = 6UL + (ulong)count * 12 + 2 + (ulong)ordinal * 4;
            if (tag.Length == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Length, rowOffset, ordinal), out diagnostic, 0, 1);
            referencedPayloadBytes += tag.Length;
            ulong payloadOffset = (ulong)StorageOffset + tag.StorageRelativeOffset;
            if (referencedPayloadBytes > (ulong)maxReferencedPayloadBytes)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.At(DataField.Payload, payloadOffset, ordinal), out diagnostic,
                    referencedPayloadBytes, (ulong)maxReferencedPayloadBytes);
            status = TryGetLanguageTagPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!IsValidUtf16BigEndian(payload))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    Location.At(DataField.Payload, payloadOffset, ordinal), out diagnostic,
                    (ulong)payload.Length);
        }

        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materialize text only for known Unicode encodings.</summary>
    public ReadStatus TryDecodeRecord(int ordinal, out string? text,
        out FontDiagnostic diagnostic)
    {
        text = null;
        ReadStatus status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!IsUtf16(record.PlatformId, record.EncodingId))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.EncodingId, 6UL + (ulong)ordinal * 12 + 2, ordinal),
                out diagnostic, record.EncodingId);
        status = TryGetRecordPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!IsValidUtf16BigEndian(payload))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                Location.At(DataField.Payload,
                    (ulong)StorageOffset + record.StorageRelativeOffset, ordinal),
                out diagnostic, record.Length);
        text = Encoding.BigEndianUnicode.GetString(payload);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool IsUtf16(ushort platformId, ushort encodingId) =>
        platformId == 0 || (platformId == 3 && encodingId is 0 or 1 or 10);

    internal static bool IsValidUtf16BigEndian(ReadOnlySpan<byte> bytes)
    {
        if ((bytes.Length & 1) != 0) return false;
        for (int i = 0; i < bytes.Length; i += 2)
        {
            ushort value = ReadWire.U16(bytes, i);
            if (value is >= 0xD800 and <= 0xDBFF)
            {
                if (bytes.Length - i < 4) return false;
                ushort low = ReadWire.U16(bytes, i + 2);
                if (low is < 0xDC00 or > 0xDFFF) return false;
                i += 2;
            }
            else if (value is >= 0xDC00 and <= 0xDFFF) return false;
        }
        return true;
    }

    private static int CompareKeys(RecordData left, RecordData right)
    {
        int comparison = left.PlatformId.CompareTo(right.PlatformId);
        if (comparison != 0) return comparison;
        comparison = left.EncodingId.CompareTo(right.EncodingId);
        if (comparison != 0) return comparison;
        comparison = left.LanguageId.CompareTo(right.LanguageId);
        return comparison != 0 ? comparison : left.NameId.CompareTo(right.NameId);
    }
}
