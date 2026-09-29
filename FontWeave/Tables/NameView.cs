using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Handwritten validation and Unicode semantics over the generated name layout.</summary>
public readonly ref partial struct NameView
{
    /// <summary>
    /// Checks structure, ordering, supported encodings and RFC 5646 tag well-formedness.
    /// Does not consult external encoding/language registries or whole-font consistency.
    /// Repeated payload references count separately toward the scan budget.
    /// </summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic,
        int maxReferencedPayloadBytes = 8 * 1024 * 1024) =>
        ValidateCore(out diagnostic, maxReferencedPayloadBytes, requireSorted: true, null);

    /// <summary>Strict validation with explicit per-original-ordinal Apple encoding selections.
    /// Undefined single-byte slots remain raw/UnmappedCharacter, not replacement text.</summary>
    public ReadStatus Validate(ReadOnlySpan<NameLegacyEncodingSelection> selections,
        out FontDiagnostic diagnostic, int maxReferencedPayloadBytes = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferencedPayloadBytes);
        ReadStatus status = TryCreateSelections(selections, null, out var encodings, out diagnostic);
        return status == ReadStatus.Done
            ? ValidateCore(out diagnostic, maxReferencedPayloadBytes, true, encodings) : status;
    }

    private ReadStatus ValidateCore(out FontDiagnostic diagnostic,
        int maxReferencedPayloadBytes, bool requireSorted, NameLegacyEncoding?[]? encodings)
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
        string? variationPrefix = null;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong rowOffset = 6UL + (ulong)ordinal * 12;
            if (requireSorted && ordinal > 0 && CompareKeys(previous, record) > 0)
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
            DataLocation payloadLocation = Location.At(DataField.Payload, payloadOffset, ordinal);
            NameLegacyEncoding? legacyEncoding = encodings?[ordinal];
            status = NameTextCodec.ValidatePayload(record.PlatformId, record.EncodingId,
                payload, payloadLocation, out diagnostic, legacyEncoding);
            if (status != ReadStatus.Done)
            {
                if (legacyEncoding is null) diagnostic = diagnostic with { Detail = record.Length };
                return status;
            }
            if (NameRecordSemantics.HasTextConstraint(record.NameId) &&
                NameTextCodec.DecodeState(record.PlatformId, record.EncodingId, payload,
                    legacyEncoding) == NameDecodeState.Decoded)
            {
                _ = NameTextCodec.TryDecode(record.PlatformId, record.EncodingId, payload,
                    legacyEncoding, out string? text, out _, payloadLocation);
                if (!NameRecordSemantics.IsValid(record.NameId, text!) ||
                    record.NameId == 25 && variationPrefix is not null && text != variationPrefix)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        payloadLocation, out diagnostic, record.NameId);
                if (record.NameId == 25) variationPrefix = text;
            }
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
            if (!NameLanguageTagSyntax.IsWellFormed(Encoding.BigEndianUnicode.GetString(payload)))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, payloadOffset, ordinal), out diagnostic);
        }

        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materialize text only for explicitly supported strict encodings.</summary>
    public ReadStatus TryDecodeRecord(int ordinal, out string? text,
        out FontDiagnostic diagnostic)
    {
        text = null;
        ReadStatus status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!NameTextCodec.IsSupported(record.PlatformId, record.EncodingId))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.EncodingId, 6UL + (ulong)ordinal * 12 + 2, ordinal),
                out diagnostic, record.EncodingId);
        status = TryGetRecordPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = NameTextCodec.TryDecode(record.PlatformId, record.EncodingId, payload,
            out text, out diagnostic, Location.At(DataField.Payload,
                (ulong)StorageOffset + record.StorageRelativeOffset, ordinal));
        if (status != ReadStatus.Done) diagnostic = diagnostic with { Detail = record.Length };
        return status;
    }

    /// <summary>Locally decode one record under a caller-selected Apple mapping.</summary>
    public ReadStatus TryDecodeRecord(int ordinal, NameLegacyEncoding encoding, out string? text,
        out FontDiagnostic diagnostic)
    {
        text = null;
        ReadStatus status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = NameTextCodec.ValidateSelection(record.PlatformId, record.EncodingId, encoding,
            DataLocation.Anonymous(0x6E616D65).At(DataField.EncodingId, 0, ordinal), out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetRecordPayload(ordinal, out ReadOnlySpan<byte> payload, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return NameTextCodec.TryDecode(encoding, payload, out text, out diagnostic,
            Location.At(DataField.Payload, (ulong)StorageOffset + record.StorageRelativeOffset, ordinal));
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
        if (!NameTextCodec.IsSupported(record.PlatformId, record.EncodingId))
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = NameTextCodec.TryDecode(record.PlatformId, record.EncodingId, payload,
            out text, out diagnostic, Location.At(DataField.Payload,
                (ulong)StorageOffset + record.StorageRelativeOffset, ordinal));
        if (status != ReadStatus.Done)
        {
            diagnostic = diagnostic with { Detail = record.Length };
            return status;
        }
        state = NameDecodeState.Decoded;
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

    public ReadStatus TryRead(ReadOnlySpan<NameLegacyEncodingSelection> selections,
        out NameTableModel? model, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = TryCreateSelections(selections, budget, out var encodings, out diagnostic);
        return status == ReadStatus.Done ? Read(out model, out diagnostic, budget, encodings) : status;
    }

    internal ReadStatus TryRead(out NameTableModel? model, out FontDiagnostic diagnostic,
        BuildBudget budget) => Read(out model, out diagnostic, budget, null);

    private ReadStatus Read(out NameTableModel? model, out FontDiagnostic diagnostic,
        BuildBudget budget, NameLegacyEncoding?[]? encodings)
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
        long tagBytes = 0;
        long constrainedTextBytes = 0;
        long legacyDecodeBuffers = 0;
        for (int i = 0; i < DeclaredCount; i++)
        {
            ulong row = 6UL + (ulong)i * 12;
            if (!budget.Work(12, Location.At(DataField.Count, row, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = TryGetRecord(i, out RecordData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += record.Length;
            if (NameRecordSemantics.HasTextConstraint(record.NameId) &&
                NameTextCodec.IsSupported(record.PlatformId, record.EncodingId))
                constrainedTextBytes += record.Length;
            legacyDecodeBuffers += NameTextCodec.LegacyDecodeBufferBytes(record.Length, encodings?[i]) *
                (NameRecordSemantics.HasTextConstraint(record.NameId) ? 2 : 1);
        }
        for (int i = 0; i < tagCount; i++)
        {
            ulong row = 6UL + (ulong)DeclaredCount * 12 + 2 + (ulong)i * 4;
            if (!budget.Work(4, Location.At(DataField.Count, row, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = TryGetLanguageTag(i, out LanguageTagData tag, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += tag.Length;
            tagBytes += tag.Length;
        }
        if (referencedBytes > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Payload, StorageOffset), out diagnostic,
                (ulong)referencedBytes, int.MaxValue);
        if (!budget.Work(referencedBytes, Location.At(DataField.Payload, StorageOffset), out diagnostic))
            return ReadStatus.ResourceLimit;
        // Admission precedes the syntax parser's strings, split arrays and duplicate sets.
        if (!budget.Buffer(tagBytes * 16 + constrainedTextBytes * 2 + legacyDecodeBuffers,
                Location.At(DataField.TemporaryBytes, StorageOffset), out diagnostic))
            return ReadStatus.ResourceLimit;
        // Unsorted records have unambiguous keys and ordinals. Preserve their source order;
        // Validate reports the wire violation and the complete-content compiler sorts stably.
        status = ValidateCore(out diagnostic, (int)referencedBytes, requireSorted: false, encodings);
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
            NameLegacyEncoding? legacyEncoding = encodings?[i];
            NameDecodeState decodeState = NameTextCodec.DecodeState(row.PlatformId, row.EncodingId,
                payload, legacyEncoding);
            string? text = null;
            if (decodeState == NameDecodeState.Decoded)
                _ = NameTextCodec.TryDecode(row.PlatformId, row.EncodingId,
                    payload, legacyEncoding, out text, out _, Location.At(DataField.Payload, payloadAt, i));
            records[i] = new NameRecordModel(i, row.PlatformId, row.EncodingId,
                row.LanguageId, row.NameId, row.Length, row.StorageRelativeOffset,
                payload.ToArray(), decodeState,
                text,
                Location.At(DataField.None, rowAt, i),
                Location.At(DataField.Payload, payloadAt, i), legacyEncoding);
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

    private ReadStatus TryCreateSelections(ReadOnlySpan<NameLegacyEncodingSelection> selections,
        BuildBudget? budget, out NameLegacyEncoding?[]? encodings, out FontDiagnostic diagnostic)
    {
        encodings = null;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (selections.IsEmpty) { diagnostic = default; return ReadStatus.Done; }
        DataLocation request = DataLocation.Anonymous(0x6E616D65);
        if (selections.Length > DeclaredCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                request.At(DataField.Count, 0), out diagnostic, (ulong)selections.Length, DeclaredCount);
        if (budget is not null &&
            (!budget.Work(selections.Length, request, out diagnostic) ||
             !budget.Buffer(DeclaredCount * 8L, request, out diagnostic))) return ReadStatus.ResourceLimit;
        var result = new NameLegacyEncoding?[DeclaredCount];
        foreach (NameLegacyEncodingSelection selection in selections)
        {
            int ordinal = selection.Ordinal;
            if (ordinal < 0 || ordinal >= DeclaredCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    request.At(DataField.Count, 0, ordinal), out diagnostic,
                    ordinal < 0 ? 0 : (ulong)ordinal, DeclaredCount);
            if (result[ordinal] is not null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    request.At(DataField.EncodingId, 0, ordinal), out diagnostic);
            status = TryGetRecord(ordinal, out RecordData record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = NameTextCodec.ValidateSelection(record.PlatformId, record.EncodingId,
                selection.Encoding, request.At(DataField.EncodingId, 0, ordinal), out diagnostic);
            if (status != ReadStatus.Done) return status;
            result[ordinal] = selection.Encoding;
        }
        encodings = result;
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
