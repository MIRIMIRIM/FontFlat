using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class HheaTableCompiler
{
    public static ReadStatus TryCompile(HheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(HheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (!budget.Work(HheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(HheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(HheaLayout.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[HheaLayout.Length];
        Write(model.Values, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(HheaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < HheaLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, HheaLayout.Length);
        Write(model.Values, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(HheaValues v, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes, v.MajorVersion);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2), v.MinorVersion);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(4), v.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(6), v.Descender);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(8), v.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(10), v.AdvanceWidthMax);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(12), v.MinLeftSideBearing);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(14), v.MinRightSideBearing);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(16), v.XMaxExtent);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(18), v.CaretSlopeRise);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(20), v.CaretSlopeRun);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(22), v.CaretOffset);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(24), v.Reserved1);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(26), v.Reserved2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(28), v.Reserved3);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(30), v.Reserved4);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(32), v.MetricDataFormat);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(34), v.NumberOfHMetrics);
    }
}
