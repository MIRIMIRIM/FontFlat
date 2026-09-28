using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Replace the Unicode text of one existing name record, identified by raw ordinal.</summary>
public readonly record struct NameReplacement(int Ordinal, string Text);

public static class NameTableCompiler
{
    public static ReadStatus TryCompile(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        Compile(input, replacements, out output, out diagnostic,
            new BuildBudget(limits), location, true);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default) =>
        Compile(input, replacements, out output, out diagnostic, budget, location, false);

    private static ReadStatus Compile(ReadOnlySpan<byte> input,
        ReadOnlySpan<NameReplacement> replacements, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location, bool chargeInput)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(0x6E616D65) : location;
        if (chargeInput && !budget.Input(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Work(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;

        ReadStatus status = NameView.TryOpen(input, out var view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        if (view.Format is not (0 or 1))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.At(DataField.Version, 0), out diagnostic, view.Format);

        int count = view.DeclaredCount;
        status = view.TryGetLanguageTagCount(out ushort tagCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = view.TryGetMetadataEnd(out ulong metadataEndRaw, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (metadataEndRaw > (ulong)input.Length || view.StorageOffset < metadataEndRaw ||
            view.StorageOffset > input.Length)
            return view.Validate(out diagnostic);
        int metadataEnd = (int)metadataEndRaw;

        long referencedBytes = 0;
        for (int i = 0; i < count; i++)
        {
            if (!budget.Work(12, location.At(DataField.Count, (ulong)(6 + i * 12), i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = view.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += record.Length;
        }
        for (int i = 0; i < tagCount; i++)
        {
            ulong row = 6UL + (ulong)count * 12 + 2 + (ulong)i * 4;
            if (!budget.Work(4, location.At(DataField.Count, row, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            status = view.TryGetLanguageTag(i, out var tag, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referencedBytes += tag.Length;
        }
        if (!budget.Work(referencedBytes, location.At(DataField.Payload, view.StorageOffset), out diagnostic))
            return ReadStatus.ResourceLimit;
        if (referencedBytes > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.At(DataField.Payload, view.StorageOffset), out diagnostic,
                (ulong)referencedBytes, int.MaxValue);

        status = view.Validate(out diagnostic, (int)referencedBytes);
        if (status != ReadStatus.Done) return status;

        if (!budget.Buffer((long)count * IntPtr.Size, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        string?[] edits = new string?[count];
        if (!budget.Work(replacements.Length, location.At(DataField.Count, 2), out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (NameReplacement replacement in replacements)
        {
            int ordinal = replacement.Ordinal;
            if (ordinal < 0 || ordinal >= count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.At(DataField.Count, 2, ordinal), out diagnostic,
                    ordinal < 0 ? 0 : (ulong)ordinal, (ulong)count);
            if (edits[ordinal] is not null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.Count, (ulong)(6 + ordinal * 12), ordinal), out diagnostic,
                    (ulong)ordinal);
            status = view.TryGetRecord(ordinal, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!NameView.IsUtf16(record.PlatformId, record.EncodingId))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    location.At(DataField.EncodingId, (ulong)(8 + ordinal * 12), ordinal),
                    out diagnostic, record.EncodingId);
            string? text = replacement.Text;
            if (text is null)
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    location.At(DataField.Payload, (ulong)(6 + ordinal * 12), ordinal), out diagnostic);
            if (!budget.Work(text.Length, location.At(DataField.Payload, 0, ordinal), out diagnostic))
                return ReadStatus.ResourceLimit;
            if (!IsValidUtf16(text))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    location.At(DataField.Payload, (ulong)(6 + ordinal * 12), ordinal), out diagnostic,
                    (ulong)text.Length);
            if (text.Length > ushort.MaxValue / 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.Length, (ulong)(14 + ordinal * 12), ordinal),
                    out diagnostic, (ulong)text.Length * 2, ushort.MaxValue);
            edits[ordinal] = text;
        }

        int suffixStart = metadataEnd;
        int suffixLength = input.Length - suffixStart;
        if (!budget.Buffer(suffixLength, location, out diagnostic) ||
            !budget.Work(suffixLength, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] referenced = new byte[suffixLength];
        long packedLength = 0;
        for (int i = 0; i < count; i++)
        {
            status = view.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int length = edits[i] is string edit ? edit.Length * 2 : record.Length;
            if (length != 0 && packedLength > ushort.MaxValue)
                return OffsetOverflow(location, 6 + i * 12 + 10, i, packedLength, out diagnostic);
            packedLength += length;
            MarkReferenced(referenced, view.StorageOffset + record.StorageRelativeOffset - suffixStart,
                record.Length);
        }
        for (int i = 0; i < tagCount; i++)
        {
            int row = 6 + count * 12 + 2 + i * 4;
            status = view.TryGetLanguageTag(i, out var tag, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ushort length = tag.Length;
            ushort offset = tag.StorageRelativeOffset;
            if (length != 0 && packedLength > ushort.MaxValue)
                return OffsetOverflow(location, row + 2, i, packedLength, out diagnostic);
            packedLength += length;
            MarkReferenced(referenced, view.StorageOffset + offset - suffixStart, length);
        }

        int opaqueLength = 0;
        foreach (byte value in referenced)
            if (value == 0) opaqueLength++;
        long total = (long)metadataEnd + packedLength + opaqueLength;
        if (!budget.Work(total, location, out diagnostic) ||
            !budget.Output(total, location, out diagnostic) ||
            !budget.Buffer(total, location, out diagnostic))
            return ReadStatus.ResourceLimit;

        byte[] compiled = new byte[(int)total];
        BinaryPrimitives.WriteUInt16BigEndian(compiled, view.Format);
        BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(2), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(4), (ushort)metadataEnd);
        if (view.Format == 1)
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(6 + count * 12), tagCount);

        int cursor = metadataEnd;
        for (int i = 0; i < count; i++)
        {
            int row = 6 + i * 12;
            input.Slice(row, 8).CopyTo(compiled.AsSpan(row));
            status = view.TryGetRecordPayload(i, out var payload, out diagnostic);
            if (status != ReadStatus.Done) return status;
            string? edit = edits[i];
            int length = edit is null ? payload.Length : edit.Length * 2;
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 8), (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 10),
                length == 0 ? (ushort)0 : (ushort)(cursor - metadataEnd));
            if (edit is null)
                payload.CopyTo(compiled.AsSpan(cursor));
            else
                WriteUtf16BigEndian(edit, compiled.AsSpan(cursor, length));
            cursor += length;
        }
        for (int i = 0; i < tagCount; i++)
        {
            int row = 6 + count * 12 + 2 + i * 4;
            status = view.TryGetLanguageTagPayload(i, out var payload, out diagnostic);
            if (status != ReadStatus.Done) return status;
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row), (ushort)payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(compiled.AsSpan(row + 2),
                (ushort)(cursor - metadataEnd));
            payload.CopyTo(compiled.AsSpan(cursor));
            cursor += payload.Length;
        }
        for (int i = 0; i < suffixLength; i++)
            if (referenced[i] == 0) compiled[cursor++] = input[suffixStart + i];

        budget.RecordCompiledTable(compiled.Length);
        output = compiled;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void MarkReferenced(Span<byte> marks, int start, int length) =>
        marks.Slice(start, length).Fill(1);

    private static ReadStatus OffsetOverflow(DataLocation location, int rowOffset,
        int ordinal, long value, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
            location.At(DataField.Offset, (ulong)rowOffset, ordinal), out diagnostic,
            (ulong)value, ushort.MaxValue);

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
}
