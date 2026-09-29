using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class PcltTableCompiler
{
    public static ReadStatus TryCompile(PcltTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(PcltLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(PcltLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(PcltLayout.Length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[PcltLayout.Length];
        Write(model.Values, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(PcltTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < PcltLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, PcltLayout.Length);
        Write(model.Values, destination.Slice(0, PcltLayout.Length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(PcltValues v, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32BigEndian(bytes, PcltLayout.Version);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(4), v.FontNumber);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(8), v.Pitch);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(10), v.XHeight);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(12), v.Style);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(14), v.TypeFamily);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(16), v.CapHeight);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(18), v.SymbolSet);
        for (int i = 0; i < 16; i++) bytes[20 + i] = (byte)v.Typeface[i];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(36), v.CharacterComplement);
        for (int i = 0; i < 6; i++) bytes[44 + i] = (byte)v.FileName[i];
        bytes[50] = unchecked((byte)v.StrokeWeight);
        bytes[51] = unchecked((byte)v.WidthType);
        bytes[52] = v.SerifStyle;
        bytes[53] = 0;
    }
}
