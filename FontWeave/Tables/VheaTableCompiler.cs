using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class VheaTableCompiler
{
    public static ReadStatus TryCompile(VheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(VheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (!budget.Work(VheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(VheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(VheaLayout.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[VheaLayout.Length];
        Write(model.Values, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(VheaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < VheaLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, VheaLayout.Length);
        Write(model.Values, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(VheaValues v, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32BigEndian(bytes, v.VersionRaw);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(4), v.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(6), v.Descender);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(8), v.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(10), v.AdvanceHeightMax);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(12), v.MinTopSideBearing);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(14), v.MinBottomSideBearing);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(16), v.YMaxExtent);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(18), v.CaretSlopeRise);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(20), v.CaretSlopeRun);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(22), v.CaretOffset);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(24), v.Reserved1);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(26), v.Reserved2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(28), v.Reserved3);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(30), v.Reserved4);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(32), v.MetricDataFormat);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(34), v.NumOfLongVerMetrics);
    }
}
