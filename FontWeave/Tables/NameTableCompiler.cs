using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct NameReplacement(int Ordinal, string Text);
public readonly record struct NameLanguageIdChange(int Ordinal, ushort LanguageId);

/// <summary>Only existing record text/IDs and one terminal format-1 tag may change.</summary>
public sealed record NameEditPlan
{
    public IReadOnlyList<NameReplacement> Replacements { get; init; } = Array.Empty<NameReplacement>();
    public IReadOnlyList<NameLanguageIdChange> LanguageIdChanges { get; init; } = Array.Empty<NameLanguageIdChange>();
    public string? AppendLanguageTag { get; init; }
    public bool RemoveLastLanguageTag { get; init; }
}

public static partial class NameTableCompiler
{
    private const uint Tag = 0x6E616D65;

    public static ReadStatus TryCompile(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) => CompileLegacy(input, replacements,
            out output, out diagnostic, new BuildBudget(limits), location, true);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default) =>
        CompileLegacy(input, replacements, out output, out diagnostic, budget, location, false);

    private static ReadStatus CompileLegacy(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location, bool chargeInput)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(Tag) : location;
        if (!budget.Buffer((long)replacements.Length * 2 * IntPtr.Size,
                location.At(DataField.TemporaryBytes, 0), out diagnostic))
            return ReadStatus.ResourceLimit;
        return Compile(input, replacements.ToArray(), Array.Empty<NameLanguageIdChange>(), null, false,
            false, out output, out diagnostic, budget, location, chargeInput);
    }

    public static ReadStatus TryCompile(ReadOnlySpan<byte> input, NameEditPlan edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        CompilePlan(input, edit, out output, out diagnostic, new BuildBudget(limits), location, true);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input, NameEditPlan edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default) =>
        CompilePlan(input, edit, out output, out diagnostic, budget, location, false);

    private static ReadStatus CompilePlan(ReadOnlySpan<byte> input, NameEditPlan edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location, bool chargeInput)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Replacements is null || edit.LanguageIdChanges is null)
            throw new ArgumentException("Edit collections cannot be null.", nameof(edit));
        return Compile(input, edit.Replacements, edit.LanguageIdChanges,
            edit.AppendLanguageTag, edit.RemoveLastLanguageTag, true,
            out output, out diagnostic, budget, location, chargeInput);
    }

    private static ReadStatus Compile(ReadOnlySpan<byte> input,
        IReadOnlyList<NameReplacement> replacements,
        IReadOnlyList<NameLanguageIdChange> languageChanges,
        string? appendTag, bool removeLastTag, bool rejectNoOp,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location, bool chargeInput)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(Tag) : location;
        DataLocation request = DataLocation.Anonymous(Tag);
        if (chargeInput && !budget.Input(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Work(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = NameView.TryOpen(input, out NameView view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out NameTableModel? model, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        NameTableModel source = model!;
        int count = source.Records.Count;
        int oldTagCount = source.LanguageTags.Count;

        if (appendTag is not null && removeLastTag)
            return Invalid(request, out diagnostic);
        if ((appendTag is not null || removeLastTag) && source.Format != 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                request.At(DataField.Version, 0), out diagnostic, source.Format);
        if (removeLastTag && oldTagCount == 0)
            return Invalid(request.At(DataField.Count, 0), out diagnostic);
        if (appendTag is not null)
        {
            if (!budget.Work(appendTag.Length, request.At(DataField.Payload, 0), out diagnostic))
                return ReadStatus.ResourceLimit;
            if (appendTag.Length > ushort.MaxValue / 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    request.At(DataField.Length, 0), out diagnostic,
                    (ulong)appendTag.Length * 2, ushort.MaxValue);
            if (!budget.Buffer((long)appendTag.Length * 32,
                    request.At(DataField.TemporaryBytes, 0), out diagnostic))
                return ReadStatus.ResourceLimit;
            if (!IsValidUtf16(appendTag) || !IsValidNewLanguageTag(appendTag))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    request.At(DataField.Payload, 0), out diagnostic);
            if (oldTagCount > 0x7FFF)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    request.At(DataField.LanguageId, 0), out diagnostic,
                    (ulong)oldTagCount + 0x8000, ushort.MaxValue);
        }
        int finalTagCount = oldTagCount + (appendTag is null ? 0 : 1) - (removeLastTag ? 1 : 0);
        if (!budget.Work((long)replacements.Count + languageChanges.Count,
                request.At(DataField.Count, 0), out diagnostic) ||
            !budget.Buffer((long)count * (IntPtr.Size + 3),
                request.At(DataField.TemporaryBytes, 0), out diagnostic))
            return ReadStatus.ResourceLimit;
        var texts = new string?[count];
        var languageIds = new ushort[count];
        var languageEdited = new bool[count];
        bool changed = appendTag is not null || removeLastTag;

        foreach (NameReplacement replacement in replacements)
        {
            int ordinal = replacement.Ordinal;
            if (ordinal < 0 || ordinal >= count)
                return InvalidOrdinal(rejectNoOp ? request : location, ordinal, count,
                    rejectNoOp ? 0UL : 2UL, out diagnostic);
            if (texts[ordinal] is not null)
                return Invalid(rejectNoOp ? request.At(DataField.Payload, 0, ordinal) :
                    source.Records[ordinal].RowLocation.At(DataField.Count,
                        source.Records[ordinal].RowLocation.Offset, ordinal), out diagnostic);
            NameRecordModel record = source.Records[ordinal];
            // The wire remains UTF-16BE, but historical Unicode requires explicitly
            // supported character positions (ASCII, Latin1 graphics, European/Extended Latin,
            // and Hangul for Unicode 1.0). Other code pages still use complete content +
            // NameTextCodec rather than this patch API.
            if (!NameView.IsUtf16(record.PlatformId, record.EncodingId))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    rejectNoOp ? DiagnosticCode.UnsupportedOperation : DiagnosticCode.UnsupportedVersion,
                    rejectNoOp ? request.At(DataField.EncodingId, 0, ordinal) :
                        record.RowLocation.At(DataField.EncodingId, record.RowLocation.Offset + 2, ordinal),
                    out diagnostic, record.EncodingId);
            string? text = replacement.Text;
            if (text is null || !IsValidUtf16(text))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    rejectNoOp ? request.At(DataField.Payload, 0, ordinal) :
                        record.RowLocation.At(DataField.Payload, record.RowLocation.Offset, ordinal),
                    out diagnostic);
            if (NameTextCodec.IsHistoricalUnicode(record.PlatformId, record.EncodingId))
            {
                DataLocation textLocation = rejectNoOp ? request.At(DataField.Payload, 0, ordinal) :
                    record.RowLocation.At(DataField.Payload, record.RowLocation.Offset, ordinal);
                if (!budget.Work(text.Length, textLocation, out diagnostic)) return ReadStatus.ResourceLimit;
                status = NameHistoricalUnicodeCodec.ValidateText(text, record.EncodingId == 0,
                    textLocation, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            if (!NameRecordSemantics.IsValid(record.NameId, text))
                return Invalid(request.At(DataField.Payload, 0, ordinal), out diagnostic);
            if (!budget.Work(text.Length, request.At(DataField.Payload, 0, ordinal), out diagnostic))
                return ReadStatus.ResourceLimit;
            if (text.Length > ushort.MaxValue / 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    rejectNoOp ? request.At(DataField.Length, 0, ordinal) :
                        record.RowLocation.At(DataField.Length, record.RowLocation.Offset + 8, ordinal),
                    out diagnostic,
                    (ulong)text.Length * 2, ushort.MaxValue);
            texts[ordinal] = text;
            changed |= text != record.Text;
        }
        foreach (NameLanguageIdChange change in languageChanges)
        {
            int ordinal = change.Ordinal;
            if (ordinal < 0 || ordinal >= count)
                return InvalidOrdinal(request, ordinal, count, 0, out diagnostic);
            if (languageEdited[ordinal])
                return Invalid(request.At(DataField.LanguageId, 0, ordinal), out diagnostic);
            languageEdited[ordinal] = true;
            languageIds[ordinal] = change.LanguageId;
            NameRecordModel record = source.Records[ordinal];
            changed |= change.LanguageId != record.LanguageId;
            if (source.Format == 0 && change.LanguageId >= 0x8000)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    request.At(DataField.LanguageId, 0, ordinal), out diagnostic,
                    change.LanguageId);
            if (change.LanguageId < 0x8000)
            {
                bool found = false;
                foreach (NameRecordModel original in source.Records)
                    if (original.PlatformId == record.PlatformId &&
                        original.EncodingId == record.EncodingId &&
                        original.LanguageId == change.LanguageId)
                    { found = true; break; }
                if (!found)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        request.At(DataField.LanguageId, 0, ordinal), out diagnostic,
                        change.LanguageId);
            }
        }
        if (rejectNoOp && !changed)
            return Invalid(request, out diagnostic);
        bool newTagReferenced = false;
        string? variationPrefix = null;
        for (int i = 0; i < count; i++)
        {
            NameRecordModel record = source.Records[i];
            string? finalText = texts[i] ?? record.Text;
            if (record.NameId == 25 && finalText is not null)
            {
                if (variationPrefix is not null && variationPrefix != finalText)
                    return Invalid(request.At(DataField.Payload, 0, i), out diagnostic);
                variationPrefix = finalText;
            }
            ushort languageId = languageEdited[i] ? languageIds[i] : record.LanguageId;
            if (source.Format == 1 && languageId >= 0x8000)
            {
                int tagIndex = languageId - 0x8000;
                if (tagIndex >= finalTagCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        languageEdited[i] ? request.At(DataField.LanguageId, 0, i) :
                            record.RowLocation.At(DataField.LanguageId, record.RowLocation.Offset + 4, i),
                        out diagnostic, (ulong)tagIndex, (ulong)finalTagCount);
                newTagReferenced |= appendTag is not null && tagIndex == oldTagCount;
            }
            if (i > 0 && CompareKeys(source.Records[i - 1],
                    languageEdited[i - 1] ? languageIds[i - 1] : source.Records[i - 1].LanguageId,
                    record, languageId) > 0)
                return Invalid(languageEdited[i] ? request.At(DataField.LanguageId, 0, i) :
                    request.At(DataField.LanguageId, 0, i - 1), out diagnostic);
        }
        if (appendTag is not null && !newTagReferenced)
            return Invalid(request.At(DataField.LanguageId, 0), out diagnostic);

        int metadataEnd = checked(6 + count * 12 + (source.Format == 1 ? 2 + finalTagCount * 4 : 0));
        if (metadataEnd > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                request.At(DataField.StorageOffset, 0), out diagnostic,
                (ulong)metadataEnd, ushort.MaxValue);
        int oldMetadataEnd = 6 + count * 12 + (source.Format == 1 ? 2 + oldTagCount * 4 : 0);
        int suffixLength = source.OriginalBytes.Length - oldMetadataEnd;
        if (!budget.Buffer(suffixLength, request.At(DataField.TemporaryBytes, 0), out diagnostic) ||
            !budget.Work(suffixLength, request.At(DataField.WorkUnits, 0), out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] referenced = new byte[suffixLength];
        long packedLength = 0;
        for (int i = 0; i < count; i++)
        {
            NameRecordModel record = source.Records[i];
            int length = texts[i] is string replacement ? replacement.Length * 2 : record.Length;
            if (length != 0 && packedLength > ushort.MaxValue)
                return OffsetOverflow(rejectNoOp ? request.At(DataField.Offset, 0, i) :
                    record.RowLocation.At(DataField.Offset, record.RowLocation.Offset + 10, i),
                    packedLength, out diagnostic);
            packedLength += length;
            MarkReferenced(referenced, source.StorageOffset + record.StorageRelativeOffset - oldMetadataEnd,
                record.Length);
        }
        for (int i = 0; i < oldTagCount; i++)
        {
            NameLanguageTagModel tag = source.LanguageTags[i];
            if (i < finalTagCount)
            {
                if (packedLength > ushort.MaxValue)
                    return OffsetOverflow(rejectNoOp ? request.At(DataField.Offset, 0, i) :
                        tag.RowLocation.At(DataField.Offset, tag.RowLocation.Offset + 2, i),
                        packedLength, out diagnostic);
                packedLength += tag.Length;
            }
            MarkReferenced(referenced, source.StorageOffset + tag.StorageRelativeOffset - oldMetadataEnd,
                tag.Length);
        }
        if (appendTag is not null)
        {
            if (packedLength > ushort.MaxValue)
                return OffsetOverflow(request.At(DataField.Offset, 0, oldTagCount),
                    packedLength, out diagnostic);
            packedLength += appendTag.Length * 2;
        }
        int opaqueLength = 0;
        foreach (byte mark in referenced)
            if (mark == 0) opaqueLength++;
        long total = (long)metadataEnd + packedLength + opaqueLength;
        if (!budget.Work(total, request.At(DataField.WorkUnits, 0), out diagnostic) ||
            !budget.Output(total, request.At(DataField.OutputBytes, 0), out diagnostic) ||
            !budget.Buffer(total, request.At(DataField.TemporaryBytes, 0), out diagnostic))
            return ReadStatus.ResourceLimit;

        byte[] compiled = new byte[(int)total];
        BinaryPrimitives.WriteUInt16BigEndian(compiled, source.Format);
        BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(2), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(4), (ushort)metadataEnd);
        if (source.Format == 1)
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(6 + count * 12), (ushort)finalTagCount);
        int cursor = metadataEnd;
        for (int i = 0; i < count; i++)
        {
            NameRecordModel record = source.Records[i];
            int row = 6 + i * 12;
            source.OriginalBytes.Span.Slice(row, 8).CopyTo(compiled.AsSpan(row));
            if (languageEdited[i])
                BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 4), languageIds[i]);
            string? replacement = texts[i];
            int length = replacement is null ? record.Length : replacement.Length * 2;
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 8), (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 10),
                length == 0 ? (ushort)0 : (ushort)(cursor - metadataEnd));
            if (replacement is null)
                record.Payload.Span.CopyTo(compiled.AsSpan(cursor));
            else if (NameTextCodec.IsHistoricalUnicode(record.PlatformId, record.EncodingId))
                NameHistoricalUnicodeCodec.Write(replacement, compiled.AsSpan(cursor, length),
                    record.EncodingId == 0);
            else
                WriteUtf16BigEndian(replacement, compiled.AsSpan(cursor, length));
            cursor += length;
        }
        for (int i = 0; i < finalTagCount; i++)
        {
            int row = 6 + count * 12 + 2 + i * 4;
            ReadOnlySpan<byte> payload = i < oldTagCount ? source.LanguageTags[i].Payload.Span : default;
            int length = i < oldTagCount ? payload.Length : appendTag!.Length * 2;
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row), (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 2), (ushort)(cursor - metadataEnd));
            if (i < oldTagCount)
                payload.CopyTo(compiled.AsSpan(cursor));
            else
                WriteUtf16BigEndian(appendTag!, compiled.AsSpan(cursor, length));
            cursor += length;
        }
        for (int i = 0; i < referenced.Length; i++)
            if (referenced[i] == 0)
                compiled[cursor++] = source.OriginalBytes.Span[oldMetadataEnd + i];
        budget.RecordCompiledTable(compiled.Length);
        output = compiled;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Invalid(DataLocation request, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue, request, out diagnostic);

    private static ReadStatus InvalidOrdinal(DataLocation request, int ordinal, int count,
        ulong offset,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            request.At(DataField.Count, offset, ordinal), out diagnostic,
            ordinal < 0 ? 0 : (ulong)ordinal, (ulong)count);

    private static ReadStatus OffsetOverflow(DataLocation at, long offset,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
            at, out diagnostic,
            (ulong)offset, ushort.MaxValue);

    private static void MarkReferenced(Span<byte> marks, int start, int length) =>
        marks.Slice(start, length).Fill(1);

    private static int CompareKeys(NameRecordModel left, ushort leftLanguage,
        NameRecordModel right, ushort rightLanguage)
    {
        int comparison = left.PlatformId.CompareTo(right.PlatformId);
        if (comparison != 0) return comparison;
        comparison = left.EncodingId.CompareTo(right.EncodingId);
        if (comparison != 0) return comparison;
        comparison = leftLanguage.CompareTo(rightLanguage);
        return comparison != 0 ? comparison : left.NameId.CompareTo(right.NameId);
    }

    private static bool IsValidUtf16(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (++i == text.Length || !char.IsLowSurrogate(text[i])) return false;
            }
            else if (char.IsLowSurrogate(c)) return false;
        }
        return true;
    }

    private static void WriteUtf16BigEndian(string text, Span<byte> destination)
    {
        for (int i = 0; i < text.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * 2, 2), text[i]);
    }

    private static bool IsValidNewLanguageTag(string tag)
    {
        string[] parts = tag.Split('-');
        if (parts[0].Length is < 2 or > 8 || !AllLetters(parts[0])) return false;
        int i = 1;
        if (i < parts.Length && parts[i].Length == 4 && AllLetters(parts[i])) i++;
        if (i < parts.Length &&
            ((parts[i].Length == 2 && AllLetters(parts[i])) ||
             (parts[i].Length == 3 && AllDigits(parts[i])))) i++;
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (; i < parts.Length; i++)
        {
            string part = parts[i];
            bool variant = (part.Length is >= 5 and <= 8 && AllLettersOrDigits(part)) ||
                (part.Length == 4 && IsDigit(part[0]) && AllLettersOrDigits(part));
            if (!variant || !variants.Add(part)) return false;
        }
        return true;
    }

    private static bool AllLetters(string value)
    {
        foreach (char c in value) if (!IsLetter(c)) return false;
        return true;
    }

    private static bool AllDigits(string value)
    {
        foreach (char c in value) if (!IsDigit(c)) return false;
        return true;
    }

    private static bool AllLettersOrDigits(string value)
    {
        foreach (char c in value) if (!IsLetter(c) && !IsDigit(c)) return false;
        return true;
    }

    private static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
