using System.Buffers.Binary;
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
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), content.Flags);
        int at = DsigLayout.HeaderLength, blockAt = directoryEnd;
        foreach (ReadOnlyMemory<byte> packet in packets)
        {
            int blockLength = DsigLayout.BlockHeaderLength + packet.Length;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), 1);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 4), (uint)blockLength);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 8), (uint)blockAt);
            // The zero-initialized output provides both reserved uint16 values.
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(blockAt + 4), (uint)packet.Length);
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
        BinaryPrimitives.WriteUInt32BigEndian(bytes, model.Version);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(4), model.NumSignatures);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(6), model.Flags);
        int at = DsigLayout.HeaderLength;
        foreach (var record in model.Records)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(at), record.Format);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(at + 4), record.Length);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(at + 8), record.Offset);
            var block = bytes.Slice((int)record.Offset, (int)record.Length);
            BinaryPrimitives.WriteUInt16BigEndian(block, 0);
            BinaryPrimitives.WriteUInt16BigEndian(block.Slice(2), 0);
            BinaryPrimitives.WriteUInt32BigEndian(block.Slice(4), (uint)record.Signature.Length);
            record.Signature.CopyTo(block.Slice(DsigLayout.BlockHeaderLength));
            at += DsigLayout.RecordStride;
        }
    }
}
