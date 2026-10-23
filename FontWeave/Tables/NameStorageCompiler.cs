using FontWeave.Core;

namespace FontWeave.Tables;

public static partial class NameTableCompiler
{
    internal static ReadStatus TryCompactStorage(ReadOnlySpan<byte> input,
        BuildBudget budget, DataLocation location, out byte[]? output,
        out FontDiagnostic diagnostic)
    {
        output = null;
        diagnostic = default;
        if (NameView.TryOpen(input, out var view, out _, location) != ReadStatus.Done ||
            view.Format is not (0 or 1) ||
            view.TryGetMetadataEnd(out ulong metadata, out _) != ReadStatus.Done ||
            metadata > ushort.MaxValue || metadata > (ulong)input.Length ||
            view.StorageOffset < metadata || view.StorageOffset > input.Length ||
            view.TryGetLanguageTagCount(out ushort tagCount, out _) != ReadStatus.Done)
            return ReadStatus.Done;

        int metadataEnd = (int)metadata;
        int count = view.DeclaredCount;
        int payloadCount = count + tagCount;
        if (!budget.Work(metadataEnd, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long payloadLength = 0;
        for (int i = 0; i < payloadCount; i++)
        {
            ReadStatus status = i < count
                ? view.TryGetRecordPayload(i, out var payload, out _, out _)
                : view.TryGetLanguageTagPayload(i - count, out payload, out _, out _);
            if (status != ReadStatus.Done) return ReadStatus.Done;
            payloadLength += payload.Length;
        }
        if (!budget.Work(input.Length + payloadLength, location, out diagnostic) ||
            !budget.Buffer(input.Length + (long)(input.Length - metadataEnd) +
                payloadCount * (long)(2 * IntPtr.Size + 8), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] source = input.ToArray();
        var referenced = new byte[input.Length - metadataEnd];
        var payloads = new ReadOnlyMemory<byte>[payloadCount];
        var offsets = new int[payloadCount];
        var order = new int[payloadCount];
        for (int i = 0; i < payloadCount; i++)
        {
            int start, length;
            if (i < count)
            {
                _ = view.TryGetRecord(i, out var record, out _);
                start = view.StorageOffset + record.StorageRelativeOffset;
                length = record.Length;
            }
            else
            {
                _ = view.TryGetLanguageTag(i - count, out var tag, out _);
                start = view.StorageOffset + tag.StorageRelativeOffset;
                length = tag.Length;
            }
            payloads[i] = source.AsMemory(start, length);
            referenced.AsSpan(start - metadataEnd, length).Fill(1);
            order[i] = i;
        }
        if (!budget.Work(payloadCount * (long)(1 + System.Numerics.BitOperations.Log2(
                (uint)Math.Max(payloadCount, 1))), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        Array.Sort(order, (a, b) =>
        {
            int comparison = payloads[b].Length.CompareTo(payloads[a].Length);
            return comparison != 0 ? comparison : a.CompareTo(b);
        });
        int capacity = (int)Math.Min(payloadLength, 2L * ushort.MaxValue);
        if (!budget.Buffer(capacity, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var storage = new byte[capacity];
        ReadStatus poolStatus = PoolNamePayloads(payloads, order, offsets, storage,
            budget, location, out int storageLength, out diagnostic);
        if (poolStatus == ReadStatus.InvalidOffset)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (poolStatus != ReadStatus.Done) return poolStatus;
        int opaqueLength = 0;
        foreach (byte mark in referenced)
            if (mark == 0) opaqueLength++;
        long total = (long)metadataEnd + storageLength + opaqueLength;
        if (total >= input.Length) return ReadStatus.Done;
        if (!budget.Work(total, location, out diagnostic) ||
            !budget.Buffer(total, location, out diagnostic) ||
            !budget.Output(total, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var compiled = new byte[(int)total];
        source.AsSpan(0, metadataEnd).CopyTo(compiled);
        _ = NameView.TryWriteStorageOffset(compiled, (ushort)metadataEnd, out _);
        for (int i = 0; i < count; i++)
        {
            _ = view.TryGetRecord(i, out var record, out _);
            _ = NameView.TryWriteRecordAt(compiled, (ulong)(6 + i * 12),
                new NameView.RecordData(i, record.PlatformId, record.EncodingId,
                    record.LanguageId, record.NameId, record.Length, (ushort)offsets[i]), out _);
        }
        for (int i = 0; i < tagCount; i++)
        {
            _ = view.TryGetLanguageTag(i, out var tag, out _);
            _ = NameView.TryWriteLanguageTagAt(compiled, (ulong)(8 + count * 12 + i * 4),
                new NameView.LanguageTagData(i, tag.Length, (ushort)offsets[count + i]), out _);
        }
        storage.AsSpan(0, storageLength).CopyTo(compiled.AsSpan(metadataEnd));
        int cursor = metadataEnd + storageLength;
        for (int i = 0; i < referenced.Length; i++)
            if (referenced[i] == 0) compiled[cursor++] = source[metadataEnd + i];
        budget.RecordCompiledTable(compiled.Length);
        output = compiled;
        return ReadStatus.Done;
    }

    private static ReadStatus PoolNamePayloads(ReadOnlyMemory<byte>[] payloads,
        int[] order, int[] offsets, byte[] storage, BuildBudget budget, DataLocation location,
        out int storageLength, out FontDiagnostic diagnostic)
    {
        storageLength = 0;
        diagnostic = default;
        foreach (int i in order)
        {
            ReadOnlySpan<byte> payload = payloads[i].Span;
            if (payload.IsEmpty) { offsets[i] = 0; continue; }
            if (!budget.Work(storageLength + payload.Length,
                    location.At(DataField.Payload, 0, i), out diagnostic))
                return ReadStatus.ResourceLimit;
            int searchLength = Math.Min(storageLength, ushort.MaxValue + payload.Length);
            int offset = storage.AsSpan(0, searchLength).IndexOf(payload);
            if (offset < 0)
            {
                offset = storageLength;
                if (offset > ushort.MaxValue)
                    return OffsetOverflow(location.At(DataField.Offset, 0, i), offset, out diagnostic);
                payload.CopyTo(storage.AsSpan(storageLength));
                storageLength += payload.Length;
            }
            offsets[i] = offset;
        }
        return ReadStatus.Done;
    }
}
