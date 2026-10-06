using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Structural DSIG writing, not signing or re-signing. Existing models reencode
/// byte-exactly; complete content uses a canonical layout. Font edits still require an
/// Operations-level reject/delete decision.</summary>
public static class DsigTableCompiler
{
    /// <summary>Compile caller-supplied version 1 / format 1 content. Directory and blocks
    /// follow caller order without sharing or alignment; opaque bytes follow the blocks.
    /// No cryptographic validity, certificate trust or full-font publication is established.</summary>
    public static ReadStatus TryCompile(DsigTableContent content, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Signatures);
        output = null;
        DataLocation request = DataLocation.Anonymous(DsigLayout.Tag);
        var budget = new BuildBudget(limits);
        if ((content.Flags & ~1) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                request.At(DataField.Flags, 6), out diagnostic, content.Flags, 1);
        int count = content.Signatures.Count;
        if (count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                request.At(DataField.Count, 4), out diagnostic, (ulong)count, ushort.MaxValue);
        if (!budget.Work(count, request, out diagnostic) ||
            !budget.Buffer((IntPtr.Size + 8L) * count, request, out diagnostic))
            return ReadStatus.ResourceLimit;

        // Capture the list's values once before measuring/writing the independent output.
        var packets = new ReadOnlyMemory<byte>[count];
        ReadOnlyMemory<byte> opaque = content.UnreferencedBytes;
        int directoryEnd = DsigLayout.HeaderLength + count * DsigLayout.RecordStride;
        long length = directoryEnd + count * (long)DsigLayout.BlockHeaderLength + opaque.Length;
        for (int i = 0; i < count; i++)
        {
            packets[i] = content.Signatures[i];
            length += packets[i].Length;
        }
        if (length > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                request.At(DataField.InputBytes, 0), out diagnostic, (ulong)length, int.MaxValue);
        if (!budget.Input((int)length, request, out diagnostic) ||
            !budget.Work(length, request, out diagnostic) ||
            !budget.Output(length, request, out diagnostic) ||
            !budget.Buffer(length, request, out diagnostic)) return ReadStatus.ResourceLimit;

        var bytes = new byte[(int)length];
        RequireWritten(DsigView.TryWriteVersion(bytes, 1, out _, request));
        RequireWritten(DsigView.TryWriteNumSignatures(bytes, (ushort)count, out _, request));
        RequireWritten(DsigView.TryWriteFlags(bytes, content.Flags, out _, request));
        int at = DsigLayout.HeaderLength, blockAt = directoryEnd;
        for (int i = 0; i < count; i++)
        {
            ReadOnlyMemory<byte> packet = packets[i];
            int blockLength = DsigLayout.BlockHeaderLength + packet.Length;
            RequireWritten(DsigView.TryWriteSignatureAt(bytes, (ulong)at,
                new DsigView.SignatureData(i, 1, (uint)blockLength, (uint)blockAt), out _, request));
            // The generated writer explicitly writes both reserved uint16 zeros.
            RequireWritten(DsigView.TryWriteSignatureBlockHeaderAt(bytes, (ulong)blockAt,
                new DsigView.SignatureBlockHeaderData(i, 0, 0, (uint)packet.Length), out _, request));
            packet.Span.CopyTo(bytes.AsSpan(blockAt + DsigLayout.BlockHeaderLength));
            at += DsigLayout.RecordStride;
            blockAt += blockLength;
        }
        opaque.Span.CopyTo(bytes.AsSpan(blockAt));
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(DsigTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = model.OriginalBytes.Length;
        var budget = new BuildBudget(limits);
        if (!budget.Work(2L * length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        Write(model, bytes);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(DsigTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = model.OriginalBytes.Length;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(DsigTable model, Span<byte> bytes)
    {
        // Preserve gaps and tails without treating them as another signature payload.
        model.OriginalBytes.CopyTo(bytes);
        // After the admission/capacity preflight these generated writers cannot fail;
        // a failure is a writer contract bug and surfaces loudly.
        RequireWritten(DsigView.TryWriteVersion(bytes, model.Version, out _, model.Location));
        RequireWritten(DsigView.TryWriteNumSignatures(bytes, model.NumSignatures, out _, model.Location));
        RequireWritten(DsigView.TryWriteFlags(bytes, model.Flags, out _, model.Location));
        int at = DsigLayout.HeaderLength;
        int ordinal = 0;
        foreach (var record in model.Records)
        {
            RequireWritten(DsigView.TryWriteSignatureAt(bytes, (ulong)at, new DsigView.SignatureData(
                ordinal, record.Format, record.Length, record.Offset), out _, model.Location));
            RequireWritten(DsigView.TryWriteSignatureBlockHeaderAt(bytes, (ulong)record.Offset,
                new DsigView.SignatureBlockHeaderData(ordinal, 0, 0, (uint)record.Signature.Length),
                out _, model.Location));
            record.Signature.CopyTo(bytes.Slice((int)record.Offset + DsigLayout.BlockHeaderLength,
                (int)record.Length - DsigLayout.BlockHeaderLength));
            at += DsigLayout.RecordStride;
            ordinal++;
        }
    }

    // Same contract as the layout emission path: a preflighted dsig write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted dsig write failed.");
    }
}
