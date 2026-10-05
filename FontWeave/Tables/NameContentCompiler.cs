using System.Buffers.Binary;
using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

public static partial class NameTableCompiler
{
    /// <summary>
    /// Compile a complete format 0/1 value. Record keys are stably sorted, tags keep their
    /// order, equal/contained payloads share storage, and opaque bytes follow that storage.
    /// No record, tag or reference is inferred or silently removed.
    /// </summary>
    public static ReadStatus TryCompile(NameTableContent content, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Records);
        ArgumentNullException.ThrowIfNull(content.LanguageTags);
        output = null;
        DataLocation request = DataLocation.Anonymous(Tag);
        ReadStatus policyStatus = NameTextCodec.ValidateUnicodeInterpretation(content.UnicodeInterpretation, request, out diagnostic);
        if (policyStatus != ReadStatus.Done) return policyStatus;
        var budget = new BuildBudget(limits);
        if (content.Format is not (0 or 1))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                request.At(DataField.Version, 0), out diagnostic, content.Format);
        int count = content.Records.Count, tagCount = content.LanguageTags.Count;
        if (content.Format == 0 && tagCount != 0)
            return Invalid(request.At(DataField.Count, 0), out diagnostic);
        long metadata = 6L + count * 12L + (content.Format == 1 ? 2L + tagCount * 4L : 0);
        if (count > ushort.MaxValue || tagCount > 0x8000 || metadata > ushort.MaxValue)
            return OffsetOverflow(request.At(DataField.StorageOffset, 0), metadata, out diagnostic);
        int metadataEnd = (int)metadata;
        if (!budget.Work(count + tagCount, request, out diagnostic) ||
            !budget.Buffer((count + tagCount) * (long)(2 * IntPtr.Size + 8) + count * 4L,
                request, out diagnostic))
            return ReadStatus.ResourceLimit;
        var payloads = new ReadOnlyMemory<byte>[count + tagCount];
        var offsets = new int[payloads.Length];
        var payloadOrder = new int[payloads.Length];
        var recordOrder = new int[count];
        long payloadLength = 0;
        string? variationPrefix = null;
        for (int i = 0; i < count; i++)
        {
            NameRecordValue record = content.Records[i];
            if (record.LanguageId >= 0x8000 &&
                (content.Format == 1 ? record.LanguageId - 0x8000 >= tagCount :
                    record.PlatformId is < 240 or > 255))
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    request.At(DataField.LanguageId, 0, i), out diagnostic, record.LanguageId,
                    (ulong)tagCount);
            if (record.Payload.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    request.At(DataField.Length, 0, i), out diagnostic,
                    (ulong)record.Payload.Length, ushort.MaxValue);
            if (!budget.Work(record.Payload.Length, request.At(DataField.Payload, 0, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            DataLocation payloadLocation = request.At(DataField.Payload, 0, i);
            if (record.LegacyEncoding is NameLegacyEncoding legacyEncoding)
            {
                ReadStatus selectionStatus = NameTextCodec.ValidateSelection(record.PlatformId,
                    record.EncodingId, legacyEncoding, request.At(DataField.EncodingId, 0, i), out diagnostic);
                if (selectionStatus != ReadStatus.Done) return selectionStatus;
            }
            ReadStatus status = NameTextCodec.ValidatePayload(record.PlatformId, record.EncodingId,
                record.Payload.Span, payloadLocation, out diagnostic, record.LegacyEncoding, content.UnicodeInterpretation);
            if (status != ReadStatus.Done) return status;
            if (NameRecordSemantics.HasTextConstraint(record.NameId) &&
                NameTextCodec.DecodeState(record.PlatformId, record.EncodingId, record.Payload.Span,
                    record.LegacyEncoding, content.UnicodeInterpretation) == NameDecodeState.Decoded)
            {
                if (!budget.Buffer(Math.Max(record.Payload.Length * 2L,
                        NameTextCodec.AdditionalDecodeBufferBytes(record.PlatformId, record.EncodingId,
                            record.Payload.Length, record.LegacyEncoding, content.UnicodeInterpretation)),
                        request, out diagnostic))
                    return ReadStatus.ResourceLimit;
                _ = NameTextCodec.TryDecodeSemantic(record.PlatformId, record.EncodingId, record.Payload.Span,
                    record.LegacyEncoding, out string? text, out _, payloadLocation, content.UnicodeInterpretation);
                if (!NameRecordSemantics.IsValid(record.NameId, text!) ||
                    record.NameId == 25 && variationPrefix is not null && text != variationPrefix)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        payloadLocation, out diagnostic, record.NameId);
                if (record.NameId == 25) variationPrefix = text;
            }
            payloads[i] = record.Payload;
            recordOrder[i] = i;
            payloadLength += record.Payload.Length;
        }
        for (int i = 0; i < tagCount; i++)
        {
            string? tag = content.LanguageTags[i];
            if (tag is null || tag.Length > ushort.MaxValue / 2)
                return Invalid(request.At(DataField.Length, 0, i), out diagnostic);
            if (!budget.Work(tag.Length, request.At(DataField.Payload, 0, i), out diagnostic) ||
                !budget.Buffer(tag.Length * 32L, request, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (!NameLanguageTagSyntax.IsWellFormed(tag))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    request.At(DataField.Payload, 0, i), out diagnostic);
            payloads[count + i] = Encoding.BigEndianUnicode.GetBytes(tag);
            payloadLength += tag.Length * 2;
        }
        long inputLength = metadata + payloadLength + content.UnreferencedBytes.Length;
        if (inputLength > int.MaxValue || !budget.Input((int)inputLength, request, out diagnostic))
        {
            if (inputLength > int.MaxValue)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    request.At(DataField.InputBytes, 0), out diagnostic, (ulong)inputLength, int.MaxValue);
            return ReadStatus.ResourceLimit;
        }
        // The last nonempty payload can cross 64 KiB. Only its *start* is Offset16.
        int capacity = (int)Math.Min(payloadLength, 2L * ushort.MaxValue);
        if (!budget.Buffer(capacity, request, out diagnostic)) return ReadStatus.ResourceLimit;
        var storage = new byte[capacity];
        for (int i = 0; i < payloadOrder.Length; i++) payloadOrder[i] = i;
        // Longest-first pooling also shares substring references without preserving source alias topology.
        Array.Sort(payloadOrder, (a, b) =>
        {
            int result = payloads[b].Length.CompareTo(payloads[a].Length);
            if (result == 0 && a < count && b < count)
                result = CompareContentKeys(content.Records[a], content.Records[b]);
            return result != 0 ? result : a.CompareTo(b);
        });
        int storageLength = 0;
        foreach (int i in payloadOrder)
        {
            ReadOnlySpan<byte> payload = payloads[i].Span;
            if (payload.IsEmpty) { offsets[i] = 0; continue; }
            if (!budget.Work(storageLength + payload.Length, request.At(DataField.Payload, 0, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            int searchLength = Math.Min(storageLength, ushort.MaxValue + payload.Length);
            int offset = storage.AsSpan(0, searchLength).IndexOf(payload);
            if (offset < 0)
            {
                offset = storageLength;
                if (offset > ushort.MaxValue)
                    return OffsetOverflow(request.At(DataField.Offset, 0, i), offset, out diagnostic);
                payload.CopyTo(storage.AsSpan(storageLength));
                storageLength += payload.Length;
            }
            offsets[i] = offset;
        }
        Array.Sort(recordOrder, (a, b) =>
        {
            int result = CompareContentKeys(content.Records[a], content.Records[b]);
            return result != 0 ? result : a.CompareTo(b);
        });
        long total = metadata + storageLength + content.UnreferencedBytes.Length;
        if (!budget.Work(total, request, out diagnostic) ||
            !budget.Output(total, request, out diagnostic) ||
            !budget.Buffer(total, request, out diagnostic))
            return ReadStatus.ResourceLimit;
        var compiled = new byte[(int)total];
        _ = NameView.TryWriteFormat(compiled, (ushort)content.Format, out _);
        _ = NameView.TryWriteDeclaredCount(compiled, (ushort)count, out _);
        _ = NameView.TryWriteStorageOffset(compiled, (ushort)metadataEnd, out _);
        for (int i = 0; i < count; i++)
        {
            int original = recordOrder[i];
            NameRecordValue record = content.Records[original];
            _ = NameView.TryWriteRecordAt(compiled, (ulong)(6 + i * 12), new NameView.RecordData(i,
                record.PlatformId, record.EncodingId, record.LanguageId, record.NameId,
                (ushort)record.Payload.Length, (ushort)offsets[original]), out _);
        }
        if (content.Format == 1)
        {
            // LanguageTagCount is a condition-activated scalar (WhenVersion=1)
            // and therefore has no generated writer; the raw write stays.
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(6 + count * 12), (ushort)tagCount);
            for (int i = 0; i < tagCount; i++)
            {
                _ = NameView.TryWriteLanguageTagAt(compiled, (ulong)(8 + count * 12 + i * 4),
                    new NameView.LanguageTagData(i, (ushort)payloads[count + i].Length,
                        (ushort)offsets[count + i]), out _);
            }
        }
        storage.AsSpan(0, storageLength).CopyTo(compiled.AsSpan(metadataEnd));
        content.UnreferencedBytes.Span.CopyTo(compiled.AsSpan(metadataEnd + storageLength));
        budget.RecordCompiledTable(compiled.Length);
        output = compiled;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int CompareContentKeys(NameRecordValue a, NameRecordValue b)
    {
        int comparison = a.PlatformId.CompareTo(b.PlatformId);
        if (comparison != 0) return comparison;
        comparison = a.EncodingId.CompareTo(b.EncodingId);
        if (comparison != 0) return comparison;
        comparison = a.LanguageId.CompareTo(b.LanguageId);
        return comparison != 0 ? comparison : a.NameId.CompareTo(b.NameId);
    }
}
