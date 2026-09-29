using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Byte-preserving structural reencoding, not signing or re-signing.
/// Font edits still require an Operations-level reject/delete decision.</summary>
public static class DsigTableCompiler
{
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
