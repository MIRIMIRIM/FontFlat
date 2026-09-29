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
        if (metadataEnd > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                Location.At(DataField.StorageOffset, 4), out diagnostic,
                metadataEnd, ushort.MaxValue);
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

    /// <summary>Locally decode a record without treating an unknown encoding as damaged text.</summary>
    public ReadStatus TryDecodeRecordState(int ordinal, out NameDecodeState state,
        out string? text, out FontDiagnostic diagnostic)
    {
        state = NameDecodeState.UnsupportedEncoding;
        text = null;
        ReadStatus status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetRecordPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!IsUtf16(record.PlatformId, record.EncodingId))
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!IsValidUtf16BigEndian(payload))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                Location.At(DataField.Payload,
                    (ulong)StorageOffset + record.StorageRelativeOffset, ordinal),
                out diagnostic, record.Length);
        state = NameDecodeState.Decoded;
        text = Encoding.BigEndianUnicode.GetString(payload);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(out NameTableModel? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out NameTableModel? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLanguageTagCount(out ushort tagCount, out diagnostic);
        if (status != ReadStatus.Done) return status;

        long referencedBytes = 0;
        for (int i = 0; i < DeclaredCount; i++)
        {
            ulong row = 6UL + (ulong)i * 12;
            if (!budget.Work(12, Location.At(DataField.Count, row, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = TryGetRecord(i, out RecordData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += record.Length;
        }
        for (int i = 0; i < tagCount; i++)
        {
            ulong row = 6UL + (ulong)DeclaredCount * 12 + 2 + (ulong)i * 4;
            if (!budget.Work(4, Location.At(DataField.Count, row, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = TryGetLanguageTag(i, out LanguageTagData tag, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += tag.Length;
        }
        if (referencedBytes > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Payload, StorageOffset), out diagnostic,
                (ulong)referencedBytes, int.MaxValue);
        if (!budget.Work(referencedBytes, Location.At(DataField.Payload, StorageOffset), out diagnostic))
            return ReadStatus.ResourceLimit;
        status = Validate(out diagnostic, (int)referencedBytes);
        if (status != ReadStatus.Done) return status;

        long modelArrays = (long)DeclaredCount * IntPtr.Size + (long)tagCount * IntPtr.Size;
        if (!budget.Buffer(_data.Length + modelArrays + referencedBytes,
                Location.At(DataField.TemporaryBytes, 0), out diagnostic))
            return ReadStatus.ResourceLimit;
        var records = new NameRecordModel[DeclaredCount];
        var tags = new NameLanguageTagModel[tagCount];
        byte[] original = _data.ToArray();
        for (int i = 0; i < records.Length; i++)
        {
            _ = TryGetRecord(i, out RecordData row, out _);
            _ = TryGetRecordPayload(i, out ReadOnlySpan<byte> payload, out _);
            ulong rowAt = 6UL + (ulong)i * 12;
            ulong payloadAt = (ulong)StorageOffset + row.StorageRelativeOffset;
            bool decoded = IsUtf16(row.PlatformId, row.EncodingId);
            records[i] = new NameRecordModel(i, row.PlatformId, row.EncodingId,
                row.LanguageId, row.NameId, row.Length, row.StorageRelativeOffset,
                payload.ToArray(), decoded ? NameDecodeState.Decoded : NameDecodeState.UnsupportedEncoding,
                decoded ? Encoding.BigEndianUnicode.GetString(payload) : null,
                Location.At(DataField.None, rowAt, i),
                Location.At(DataField.Payload, payloadAt, i));
        }
        for (int i = 0; i < tags.Length; i++)
        {
            _ = TryGetLanguageTag(i, out LanguageTagData row, out _);
            _ = TryGetLanguageTagPayload(i, out ReadOnlySpan<byte> payload, out _);
            ulong rowAt = 6UL + (ulong)DeclaredCount * 12 + 2 + (ulong)i * 4;
            ulong payloadAt = (ulong)StorageOffset + row.StorageRelativeOffset;
            tags[i] = new NameLanguageTagModel(i, row.Length, row.StorageRelativeOffset,
                payload.ToArray(), Encoding.BigEndianUnicode.GetString(payload),
                Location.At(DataField.None, rowAt, i),
                Location.At(DataField.Payload, payloadAt, i));
        }
        model = new NameTableModel(Format, StorageOffset, original, records, tags, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool IsUtf16(ushort platformId, ushort encodingId) =>
        (platformId == 0 && encodingId <= 4) ||
        (platformId == 3 && encodingId is 0 or 1 or 2 or 6 or 10);

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
