using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Writes a compact directory and opaque payloads in original record order.
/// Unreferenced gaps/tail bytes are not metadata and are not emitted.</summary>
public static class MetaTableCompiler
{
    public static ReadStatus TryCompile(MetaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        long length = GetLength(model);
        var budget = new BuildBudget(limits);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[(int)length];
        Write(model, bytes);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MetaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        long length = GetLength(model);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(MetaTable model, MetaEditPlan edit,
        out MetaTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Entries);
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(32L * (edit.Entries.Count + (long)model.DataMapsCount), model.Location, out diagnostic) ||
            !budget.Buffer((IntPtr.Size + 16L) * edit.Entries.Count, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var requests = edit.Entries.ToArray();
        Array.Sort(requests, static (left, right) => left.Tag.CompareTo(right.Tag));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].Tag == requests[i - 1].Tag)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    model.Location.At(DataField.Tag, 0), out diagnostic, requests[i].Tag);

        long replacementBytes = 0;
        foreach (var request in requests) replacementBytes += request.Payload.Length;
        if (!budget.Work(replacementBytes, model.Location, out diagnostic) ||
            !budget.Buffer(replacementBytes + (IntPtr.Size + 8L) * model.DataMapsCount,
                model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = model.Records.ToArray();
        int matched = 0;
        bool changed = false;
        for (int i = 0; i < records.Length; i++)
        {
            int low = 0, high = requests.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int comparison = records[i].Tag.CompareTo(requests[middle].Tag);
                if (comparison < 0) high = middle - 1;
                else if (comparison > 0) low = middle + 1;
                else
                {
                    // Snapshot mutable caller memory before testing and retaining its value.
                    byte[] payload = requests[middle].Payload.ToArray();
                    changed |= !records[i].Payload.SequenceEqual(payload);
                    records[i] = new(records[i].Tag, payload);
                    matched++;
                    break;
                }
            }
        }
        if (matched != requests.Length)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                model.Location.At(DataField.Tag, 0), out diagnostic,
                (ulong)matched, (ulong)requests.Length);
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(records, model.Location, model.SourceReserved);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long GetLength(MetaTable model)
    {
        long length = MetaLayout.HeaderLength + MetaLayout.RecordStride * (long)model.DataMapsCount;
        foreach (var record in model.Records) length += record.Payload.Length;
        return length;
    }

    private static void Write(MetaTable model, Span<byte> bytes)
    {
        // After the admission/capacity preflight these generated writers cannot fail;
        // a failure is a writer contract bug and surfaces loudly. Compiled output is
        // additionally re-opened and strictly Validated by both callers.
        RequireWritten(MetaView.TryWriteVersion(bytes, model.Version, out _, model.Location));
        RequireWritten(MetaView.TryWriteFlags(bytes, model.Flags, out _, model.Location));
        RequireWritten(MetaView.TryWriteReserved(bytes, model.Reserved, out _, model.Location));
        RequireWritten(MetaView.TryWriteDataMapsCount(bytes, model.DataMapsCount, out _, model.Location));
        int at = MetaLayout.HeaderLength;
        int payloadAt = at + MetaLayout.RecordStride * (int)model.DataMapsCount;
        int ordinal = 0;
        foreach (var record in model.Records)
        {
            RequireWritten(MetaView.TryWriteDataMapAt(bytes, (ulong)at, new MetaView.DataMapData(ordinal,
                record.Tag, (uint)payloadAt, (uint)record.Payload.Length), out _, model.Location));
            record.Payload.CopyTo(bytes.Slice(payloadAt));
            at += MetaLayout.RecordStride;
            payloadAt += record.Payload.Length;
            ordinal++;
        }
    }

    // Same contract as the layout emission path: a preflighted meta write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted meta write failed.");
    }
}
